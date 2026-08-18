# xtaf.py - basic interface for big endian FATX(XTAF) filesystem
# This file is in pre-release condition and unsupported - use at your own risk

# Copyright (C) 2026 SK1080
# 
# This program is free software; you can redistribute it and/or
# modify it under the terms of the GNU General Public License
# as published by the Free Software Foundation; either version 2
# of the License, or (at your option) any later version.
# 
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
# 
# You should have received a copy of the GNU General Public License
# along with this program; if not, see
# <https://www.gnu.org/licenses/>.

import datetime
import os
import random
import struct

# TODO: this implementation is pretty hack, fix?
# TODO: Merge/Consolidate XTAFImage / XTAFPartition
# TODO: import_folder_to_dir should use add_file_to_dir
# TODO: proper subfolder support
# TODO: proper timestamp support

class XTAFImage():
    ### Flash-backed XTAF16 Partition for Beta
    # flash objection should contain propery data which contains logicical nand contents
    # partition offset is a logical (not physical) offset
    def __init__(self, fat_size=16, fat_offset=0x1000, root_offset=0x3000, flash=None, partition_offset=0):
        print('[xtaf] init')
        self.flash = flash

        self.partition_offset = partition_offset
        self.root_dir = self.flash.data[partition_offset+root_offset:partition_offset+root_offset+0x4000]

        self.serial, self.spc, self.root_first_cluster = struct.unpack_from('!3L', self.flash.data, partition_offset+0x4)
        print('\tvolume serial: ' + hex(self.serial))
        print('\tvolume spc: ' + hex(self.spc))
        print('\tvolume rfc: ' + hex(self.root_first_cluster))
        self.cluster_sz = self.spc * 512

        self.fat = self.flash.data[partition_offset+fat_offset:partition_offset+root_offset]
        self.fat_offset = fat_offset
        self.root_offset = root_offset
        self.fat_size = (root_offset - fat_offset)
        self.fileidx = 0

    def initialize(self, spc=0x20, rfc=1):
        print(f'[xtaf] initialize partition @ {self.flash.offset_description(offset=self.partition_offset)}')

        self.spc = spc
        self.root_first_cluster = rfc
        self.cluster_sz = self.spc * 512

        serial = struct.unpack('!L', os.urandom(4))[0]
        
        struct.pack_into('!3L', self.flash.data, self.partition_offset+0x4, serial, spc, rfc)
        self.flash.data[self.partition_offset:self.partition_offset+4] = b'XTAF'

        # Zero FAT
        for i in range(self.partition_offset + self.fat_offset, self.partition_offset + self.fat_offset + self.fat_size):
            self.flash.data[i:i+1] = b'\x00'

        # Reserve non-existant first cluster and root        
        struct.pack_into('!2H', self.flash.data, self.partition_offset + self.fat_offset, 0xFFF8, 0xFFFF)

        # Reserve header + FAT + root directory in flash Spare
        rsv_start = self.partition_offset
        rsv_end = self.partition_offset + 0x3000 + self.cluster_sz

        rsv_start_page = rsv_start//0x200
        rsv_end_page = rsv_end//0x200

        if rsv_end % 0x200 != 0:
            rsv_end_page += 1

        print(f'[xtaf] reserve flash pages {hex(rsv_start_page)}:{hex(rsv_end_page)}')

        #FIXME: bad copypasta from elsewhere, on beta this gets over-written anyway
        #TODO: refactor spare reservation
        for page in range(rsv_start_page, rsv_end_page):
            if self.flash.block_size != 0x4000:
                # Create new BB FS Spare
                self.flash.spare_data[page] = bytearray(b'\x00'*0x10)
                self.flash.spare_data[page][0] = 0xFF
                self.flash.spare_data[page][0x7] = 0xA
                self.flash.spare_data[page][0x8] = self.flash.fs_size>>5
                self.flash.spare_data[page][0x9] = 0x4
            else:
                self.flash.spare_data[page] = bytearray(b'\x00'*0x10)
                self.flash.spare_data[page][self.flash.bb_marker_idx] = 0xFF

        self.fileidx = 0
    
        pass

    def allocate_chain(self, ncl):

        chain = []
        fat_off = self.partition_offset + self.fat_offset
        fat_cl = self.fat_size//2

        for i in range(0, fat_cl):
            if len(chain) == ncl:
                return chain
            cval = struct.unpack_from('!H', self.flash.data, (i*2) + fat_off)[0]
            if cval == 0:
                chain.append(i)

        if len(chain) == ncl:
            return chain
        
        raise EOFError('failed to allocate chain: out of FAT')

    def write_chain_to_fat(self, chain):
        fat_off = self.partition_offset + self.fat_offset
        
        if len(chain) > 1:
            for cidx in range(0, len(chain)-1):
                cl = chain[cidx]
                ncl = chain[cidx+1]

                struct.pack_into('!H', self.flash.data, (cl*2) + fat_off, ncl)

        last = chain[-1]
        struct.pack_into('!H', self.flash.data, (last*2) + fat_off, 0xFFFF)


    def add_file(self, path, name=None):
        print(f'[xtaf] add_file {path}')

        file_data = open(path, 'rb').read()
        file_len =  len(file_data)

        file_clusters = file_len//self.cluster_sz
        if file_len % self.cluster_sz != 0:
            file_clusters += 1
        
        print(f'\t{file_clusters} cluster(s)')
        chain = self.allocate_chain(file_clusters)
        self.write_chain_to_fat(chain)
        print('\t', chain)

        if not name:
            name =  os.path.basename(path)
        nmax = 0x3A
        if len(name) > nmax:
            name = name[:nmax]

        print(f'\tinsert as {name}')
        name = name.encode()
        timestamp = self.flash.file_timestamp_to_fat_timestamp(datetime.datetime.now().timestamp())

        entry = bytearray(b'\xFF'*0x40)
        struct.pack_into('<H', entry, 0, len(name))
        entry[2:2+len(name)] = name
        struct.pack_into('!5L', entry, 0x2C, chain[0], len(file_data), timestamp, timestamp, timestamp)

        entry_offset = self.partition_offset + self.root_offset + (self.fileidx * 0x40)
        self.fileidx += 1

        self.flash.data[entry_offset:entry_offset+0x40] = entry
        self.write_data_to_chain(file_data, chain)


        pass

    def import_fs(self, path):
        from os import listdir
        from os.path import isfile, join
        files = [f for f in listdir(path) if isfile(join(path, f))]
        for file in files:
            self.add_file(os.path.join(path, file))


    def load_chain(self, cluster):
        chain = []
        while True:
            chain.append(cluster)
            val = struct.unpack_from('!H', self.fat, 2*cluster)[0]
            if val >= 0xF000:
                break
            cluster = val
        return chain

    def read_data_from_chain(self, chain):
        data = bytearray()
        for cluster in chain:
            coff = (self.cluster_sz * (cluster-1)) + self.root_offset
            data += self.flash.data[self.partition_offset+coff:self.partition_offset+coff+self.cluster_sz]
        return data
    
    def write_data_to_chain(self, data, chain):
        for cidx in range(0, len(chain)):
            cluster = chain[cidx]
            coff = (self.cluster_sz * (cluster-1)) + self.root_offset + self.partition_offset
            #print(f'\t write data to cl {cluster} @ coff {hex(coff)}')

            cl = data[cidx*self.cluster_sz:(cidx+1)*self.cluster_sz]
            self.flash.data[coff:coff+len(cl)] = cl

            rsv_start = coff // 0x200
            rsv_end = (coff+self.cluster_sz)//0x200

            #FIXME: bad copypasta from elsewhere, on beta this gets over-written anyway
            #TODO: refactor spare reservation
            for page in range(rsv_start, rsv_end):
                if self.flash.block_size != 0x4000:
                    # Create new BB FS Spare
                    self.flash.spare_data[page] = bytearray(b'\x00'*0x10)
                    self.flash.spare_data[page][0] = 0xFF
                    self.flash.spare_data[page][0x7] = 0xA
                    self.flash.spare_data[page][0x8] = self.flash.fs_size>>5
                    self.flash.spare_data[page][0x9] = 0x4
                else:
                    self.flash.spare_data[page] = bytearray(b'\x00'*0x10)
                    self.flash.spare_data[page][self.flash.bb_marker_idx] = 0xFF



    def unpack(self, file_dir):
        print('[xtaf] unpacking...')
        if self.flash.data[self.partition_offset:self.partition_offset+4] != b'XTAF':
            print('\twrong partition magic, ending unpack')
            return

        for offset in range(0, len(self.root_dir), 0x40):
            nlen = struct.unpack_from('<H', self.root_dir, offset)[0]
            if nlen == 0xFFFF:
                continue
            name = struct.unpack_from(f'{nlen}s', self.root_dir, offset+2)[0]
            name = name.decode()
            clust, sz = struct.unpack_from('!2L', self.root_dir, offset+0x2C)

            print(f'\tfound {name} @ cluster {hex(clust)} sz {hex(sz)}')

            chain = self.load_chain(clust)
            data = self.read_data_from_chain(chain)
            open(os.path.join(file_dir, name), 'wb').write(data[:sz])

class XTAFPartition():
    ### File or disk-backed XTAF Partition (Big Endian FATX)
    ### Arguments:
    ###     store: file to read/write from
    ###     fatw: 2 for XTAF16 4 for XTAF32
    ###     fat_offset: offset to FAT table (default is and should be 0x1000 for supported partitions) relative to partition
    ###     root_offset: offset of root directory relative to partition. use 0 to auto calculate.
    ###     partition_sz: size of partition for geometry calculation. 0 may be provided if root_offset != 0.
    ###     partition_offset: global offset of partition in file
    ###     initialize: Format/Create the XTAF volume (overwrites header and FAT, default=False)
    ###     initspc: Sectors per cluster (spc) to use if formatting/creating (default 0x20)
    def __init__(self, store, fatw=4, fat_offset=0x1000, root_offset=0, partition_sz=0, partition_offset=0, initialize=False, initspc=0x20):
        if root_offset == 0:
            print('[XTAFPartition:__init__] calculate geometry')
            assert(partition_sz != 0)
            if initialize:
                spc = initspc
            else:
                store.seek(partition_offset)
                serial, spc, rfc = struct.unpack_from('!3L', store.read(0x10), 0x4)

            shifts = {
                0x10: 0xD,
                0x20: 0xE,
                0x40: 0xF,
                0x80: 0x10
            }

            cluster_sz = 512 * spc
            clusters = ((partition_sz)>>shifts[spc])+1
            print(f'\tclusters+1: {hex(clusters)}')
            if clusters < 0xFFF0:
                print('\tXTAF16')
                fatw = 2
                fat_sz = clusters << 1
            else:
                print('\tXTAF32')
                fatw = 4
                fat_sz = clusters << 2
            print(f'\tfat sz: {hex(fat_sz)}')

            root_offset = 4096 + ((0x1000 + fat_sz - 1) & ~(0x1000-1))

            print(f'\troot_offset = {hex(root_offset)}')
        else:
            print('[XTAFPartition:__init__] using provided root offset for geometry')

        self.fat_size = (root_offset - fat_offset)

        if initialize:
            print('[XTAFPartition:__init__] format partition')
            serial = random.randint(0, 4294967295)
            header = struct.pack('!4L', 0x58544146, serial, initspc, 1)
            store.seek(partition_offset)
            store.write(header)

            # Zero rest of header
            store.write(b'\x00'*(fat_offset-0x10))
            
            blank_fat = bytearray(b'\x00'*self.fat_size)

            # Mark FAT 0 RSVD, Mark 1 End [Root Dir]
            if fatw == 4:
                struct.pack_into('!2L', blank_fat, 0, 0xFFFFFFFD, 0xFFFFFFFF)
            else:
                struct.pack_into('!2H', blank_fat, 0, 0xFFFD, 0xFFFF)

            # Write FAT
            store.seek(partition_offset+fat_offset)
            store.write(blank_fat)

            # Initialize Root Directory
            store.seek(partition_offset+root_offset)
            store.write(b'\xFF'*(int(initspc*512)))


        self.store = store
        self.partition_offset = partition_offset
        self.fatw=fatw

        store.seek(partition_offset+root_offset)
        self.root_dir = store.read(0x4000)

        store.seek(partition_offset)
        self.serial, self.spc, self.root_first_cluster = struct.unpack_from('!3L', store.read(0x10), 0x4)
        print('[XTAFPartition:__init__] read header')
        print('\tvolume serial: ' + hex(self.serial))
        print('\tvolume spc: ' + hex(self.spc))
        print('\tvolume rfc: ' + hex(self.root_first_cluster))
        self.cluster_sz = self.spc * 512

        store.seek(partition_offset+fat_offset)
        self.fat = bytearray(store.read(root_offset-fat_offset))
        open('fat.open.bin', 'wb').write(self.fat)

        try:
            fixfat = open('fat.fix.bin', 'rb').read()
            self.store.seek(partition_offset+fat_offset)
            self.store.write(fixfat)
            self.fat = bytearray(fixfat)
            os.unlink('fat.fix.bin')
        except:
            pass

        self.fat_offset = fat_offset
        self.root_offset = root_offset
        self.fileidx = 0

        self.unlink_fat_set = set()


    def allocate_chain(self, ncl):
        # Creates a new FAT chain of cluster length ncl
        # Note: Doesn't modify FAT, see write_chain_to_fat
        chain = []
        fat_cl = self.fat_size//self.fatw

        for i in range(0, fat_cl):
            if len(chain) == ncl:
                return chain
            format_str = '!L'
            if self.fatw == 2:
                format_str = '!H'
            cval = struct.unpack_from(format_str, self.fat, (i*self.fatw))[0]
            if cval == 0:
                chain.append(i)

        if len(chain) == ncl:
            return chain
        
        raise EOFError('failed to allocate chain: out of FAT')

    def write_chain_to_fat(self, chain):
        # Writes a FAT chain to the in-memory FAT (self.fat)
        if len(chain) > 1:
            for cidx in range(0, len(chain)-1):
                cl = chain[cidx]
                ncl = chain[cidx+1]

                format_str = '!L'
                if self.fatw == 2:
                    format_str = '!H'
                struct.pack_into(format_str, self.fat, (cl*self.fatw), ncl)

        last = chain[-1]
        struct.pack_into('!L', self.fat, (last*self.fatw), 0xFFFFFFFF)


    def add_file(self, path, name=None):
        print(f'[xtaf] add_file {path}')

        file_data = open(path, 'rb').read()
        file_len =  len(file_data)

        file_clusters = file_len//self.cluster_sz
        if file_len % self.cluster_sz != 0:
            file_clusters += 1
        
        print(f'\t{file_clusters} cluster(s)')
        chain = self.allocate_chain(file_clusters)
        self.write_chain_to_fat(chain)
        print('\t', chain)

        if not name:
            name =  os.path.basename(path)
        nmax = 0x3A
        if len(name) > nmax:
            name = name[:nmax]

        print(f'\tinsert as {name}')
        name = name.encode()
        timestamp = self.flash.file_timestamp_to_fat_timestamp(datetime.datetime.now().timestamp())

        entry = bytearray(b'\xFF'*0x40)
        struct.pack_into('<H', entry, 0, len(name))
        entry[2:2+len(name)] = name
        struct.pack_into('!5L', entry, 0x2C, chain[0], len(file_data), timestamp, timestamp, timestamp)

        entry_offset = self.partition_offset + self.root_offset + (self.fileidx * 0x40)
        self.fileidx += 1

        self.flash.data[entry_offset:entry_offset+0x40] = entry
        self.write_data_to_chain(file_data, chain)


        pass

    def unlink_dirent_recurse_dir(self, dir):
        for fidx in range(0, len(dir)//0x40):
            offset = fidx*0x40
            nlen, flags = struct.unpack_from('!2B', dir, offset)
            if nlen == 0xFF or nlen == 0 or nlen == 0xE5:
                continue
            ename = struct.unpack_from(f'{nlen}s', dir, offset+2)[0]
            #print('namehex: ', name.hex())
            ename = ename.decode()
            eclust, esz = struct.unpack_from('!2L', dir, offset+0x2C)

            if(eclust == 0):
                continue

            for f in self.load_chain(eclust):
                self.unlink_fat_set.add(f)

            if flags & 0x10 > 0:
                print(f'{ename} isadir, recurse')
                self.unlink_dirent_recurse_dir(self.read_data_from_chain(self.load_chain(eclust)))

        pass

    def unlink_commit(self):
        print(f'[unlink_commit] unlinking', self.unlink_fat_set)
        format_str = '!L'
        if self.fatw == 2:
            format_str = '!H'
        for rem in self.unlink_fat_set:
            struct.pack_into(format_str, self.fat, rem*self.fatw, 0)
        self.unlink_fat_set = set()

    def unlink_dirent(self, offset):
        # Unlinks (deletes) a directory entry (dirent) at offset relative to partition start

        print(f'unlink_dirent @ {hex(offset)}')

        self.store.seek(self.partition_offset+offset)
        dirent = self.store.read(0x40)

        nlen, flags = struct.unpack_from('!2B', dirent, 0)
        print(f'unlink_nlen={nlen}')

        eclust, esz = struct.unpack_from('!2L', dirent, 0x2C)
        print(f'unlink_cluster={hex(eclust)}, unlink_size={(esz)}')

        if eclust == 0:
            return

        # Unlike any clusters referenced by this dirent
        for f in self.load_chain(eclust):
            self.unlink_fat_set.add(f)

        # If this is a directory, recurse and unlink any clusters referenced within
        if flags & 0x10 > 0:
            print(f'isadir, recurse')
            self.unlink_dirent_recurse_dir(self.read_data_from_chain(self.load_chain(eclust)))

        # Commit unlink list to fat
        self.unlink_commit()

        # Mark directory entry as deleted
        self.store.seek(self.partition_offset+offset)
        self.store.write(b'\xE5')

        # Save FAT (free clusters)
        self.store.seek(self.partition_offset+self.fat_offset)
        self.store.write(self.fat)

        # open('newfat.bin', 'wb').write(self.fat)
        
        pass

    def scan_dir_spare(self, dir_offset, name_to_rem=None):
        # Returns spare descriptor indexes in a directory @ dir_offset relative to partition start
        self.store.seek(self.partition_offset+dir_offset)
        dir = self.store.read(0x4000)

        spare_entries = list()

        for fidx in range(0, len(dir)//0x40):
            offset = fidx*0x40
            nlen, flags = struct.unpack_from('!2B', dir, offset)
            if nlen == 0xFF or nlen == 0 or nlen == 0xE5:
                spare_entries.append(fidx)
                continue
            ename = struct.unpack_from(f'{nlen}s', dir, offset+2)[0]
            #print('namehex: ', name.hex())
            ename = ename.decode()
            eclust, esz = struct.unpack_from('!2L', dir, offset+0x2C)

            if name_to_rem:
                if ename.lower() == name_to_rem.lower():
                    print(f'conflict @ entry {fidx}, removing')
                    self.unlink_dirent(int(dir_offset+(fidx*0x40)))
                    spare_entries.append(fidx)

        spare_entries = list(sorted(spare_entries))

        print('spare entries:', spare_entries)

        return spare_entries

    def add_file_to_dir(self, dir_offset, path):
        print(f'[xtaf] add_file_to_dir: {path}, offset={hex(dir_offset)}')
        basename = os.path.basename(path)
        print(f'{basename}')

        spare_entries = self.scan_dir_spare(dir_offset, name_to_rem=basename)
        diridx = spare_entries[0]

        print(f'\tinsert @ idx {diridx}')

        name = basename[:0x3A]

        file_data = open(path, 'rb').read()
        file_len = len(file_data)
        file_clusters = file_len//self.cluster_sz
        if file_len % self.cluster_sz != 0:
            file_clusters += 1

        print(f'insert as {name}, nclust={file_clusters}')

        if file_len == 0:
            new_file_chain = [0]
        else:
            new_file_chain = self.allocate_chain(file_clusters)
            self.write_chain_to_fat(new_file_chain)
            self.write_data_to_chain(file_data, new_file_chain)

        entry = bytearray(b'\xFF'*0x40)
        struct.pack_into('!2B', entry, 0, len(name), 0)
        entry[2:2+len(name)] = name.encode()
        struct.pack_into('!5L', entry, 0x2C, new_file_chain[0], file_len, 0, 0, 0)

        # Write dirent
        self.store.seek(self.partition_offset + dir_offset + int(diridx*0x40))
        self.store.write(entry)

        # Commit FAT
        self.store.seek(self.partition_offset + self.fat_offset)
        self.store.write(self.fat)


    def import_folder_to_dir(self, dir_offset, path, diridx=None):
        # Imports a folder at [path] into a directory at [dir_offset] relative to partition start
        # diridx is an optional directory entry index to store the directory at. If not specified, this will scan for free entries
        # Warning: if diridx is not specified, this will overwrite any conflicts (files/dirs with the same name)
        
        print(f'[xtaf] import_folder_to_dir: {path}, offset={hex(dir_offset)}')

        basename = os.path.basename(path)
        print(f'{basename}')

        if not diridx:
            spare_entries = self.scan_dir_spare(dir_offset, name_to_rem=basename)
            new_dir_idx = spare_entries[0]
        else:
            new_dir_idx = diridx

        new_dir_chain = self.allocate_chain(1)
        print('alloc new dir chain: ', new_dir_chain)
        new_dir = b'\xFF'*self.cluster_sz
        new_dir_offset = (self.cluster_sz * (new_dir_chain[0]-1)) + self.root_offset
        print(f'new dir @ {hex(new_dir_offset)}')

        # Write new dir
        self.store.seek(self.partition_offset + new_dir_offset)
        self.store.write(new_dir)

        nmax = 0x3A
        if len(basename) > nmax:
            basename = basename[:nmax]

        print(f'\tinsert as {basename}')
        name = basename.encode()

        entry = bytearray(b'\xFF'*0x40)
        struct.pack_into('!2B', entry, 0, len(name), 0x10)
        entry[2:2+len(name)] = name
        struct.pack_into('!5L', entry, 0x2C, new_dir_chain[0], 0, 0, 0, 0)

        print(f'new entry @ {new_dir_idx}, fcl={hex(new_dir_chain[0])}: ', entry.hex())

        # Write new dirent
        self.store.seek(self.partition_offset + dir_offset + (new_dir_idx*0x40))
        self.store.write(entry)

        # Save chain to FAT
        self.write_chain_to_fat(new_dir_chain)

        # Commit FAT
        self.store.seek(self.partition_offset + self.fat_offset)
        self.store.write(self.fat)

        diridx = 0
        for fname in os.listdir(path):
            subpath = os.path.join(path, fname)

            if os.path.isdir(subpath):
                print(f'{subpath} is a folder, traverse')
                self.import_folder_to_dir(new_dir_offset, subpath, diridx=diridx)

            if os.path.isfile(subpath):
                print(f'{subpath} is a file')

                name = fname[:0x3A]

                file_data = open(subpath, 'rb').read()
                file_len = len(file_data)
                file_clusters = file_len//self.cluster_sz
                if file_len % self.cluster_sz != 0:
                    file_clusters += 1

                print(f'insert as {name}, nclust={file_clusters}')

                if file_len == 0:
                    new_file_chain = [0]
                else:
                    new_file_chain = self.allocate_chain(file_clusters)
                    self.write_chain_to_fat(new_file_chain)
                    self.write_data_to_chain(file_data, new_file_chain)

                entry = bytearray(b'\xFF'*0x40)
                struct.pack_into('!2B', entry, 0, len(name), 0)
                entry[2:2+len(name)] = name.encode()
                struct.pack_into('!5L', entry, 0x2C, new_file_chain[0], file_len, 0, 0, 0)

                # Write dirent
                self.store.seek(self.partition_offset + new_dir_offset + int(diridx*0x40))
                self.store.write(entry)

                # Commit FAT
                self.store.seek(self.partition_offset + self.fat_offset)
                self.store.write(self.fat)

            diridx += 1


            print(fname)

        pass


    def load_chain(self, cluster):
        # Loads a FAT chain (list) from a start cluster
        chain = []
        format_str = '!L'
        if self.fatw == 2:
            format_str = '!H'
        while True:
            chain.append(cluster)
            val = struct.unpack_from(format_str, self.fat, self.fatw*cluster)[0]
            if (self.fatw == 4 and val >= 0xF0000000) or (self.fatw == 2 and val >= 0xF000):
                break
            cluster = val
        return chain

    def read_data_from_chain(self, chain):
        # Reads data from a FAT chain loaded by load_chain
        data = bytearray()
        for cluster in chain:
            coff = (self.cluster_sz * (cluster-1)) + self.root_offset
            self.store.seek(self.partition_offset+coff)
            data += self.store.read(self.cluster_sz)
        return data
    
    def write_data_to_chain(self, data, chain):
        # Writes data to a FAT chain created by load_chain or allocate_chain
        for cidx in range(0, len(chain)):
            cluster = chain[cidx]
            coff = (self.cluster_sz * (cluster-1)) + self.root_offset + self.partition_offset
            #print(f'\t write data to cl {cluster} @ coff {hex(coff)}')

            cl = data[cidx*self.cluster_sz:(cidx+1)*self.cluster_sz]
            self.store.seek(coff)
            self.store.write(cl)

    def unpack_dir(self, file_dir, dir=None):
        # Unpacks a directory to file_dir
        # Arguments:
        #     dir: directory listing data 
        
        for offset in range(0, len(dir), 0x40):
            nlen, flags = struct.unpack_from('!2B', dir, offset)

            if nlen == 0xFF or nlen == 0 or nlen == 0xE5:
                continue
            name = struct.unpack_from(f'{nlen}s', dir, offset+2)[0]
            #print('namehex: ', name.hex())
            name = name.decode()
            clust, sz = struct.unpack_from('!2L', dir, offset+0x2C)

            print(f'\tfound {name} @ cluster {hex(clust)} sz {hex(sz)} nlen {hex(nlen)} flags {hex(flags)}')

            if clust > 0:
                chain = self.load_chain(clust)
                data = self.read_data_from_chain(chain)
            else:
                data = b''

            if flags & 0x10 > 0:
                print(f'[XTAFPartition:unpack_dir] traverse {file_dir}/{name}')
                npath = os.path.join(file_dir, name)
                os.mkdir(npath)
                self.unpack_dir(npath, data[:0x4000])
                print(f'[XTAFPartition:unpack_dir] leave {file_dir}/{name}')

            else:
                open(os.path.join(file_dir, name), 'wb').write(data[:sz])

    def unpack(self, file_dir):
        print(f'[XTAFPartition:unpack] {file_dir}')
        # Unpacks entire filesystem to file_dir

        self.store.seek(self.partition_offset)
        if self.store.read(4) != b'XTAF':
            print('\twrong partition magic, ending unpack')
            return

        self.unpack_dir(file_dir, self.root_dir)
