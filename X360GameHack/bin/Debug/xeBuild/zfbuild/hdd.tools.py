#!/usr/bin/env python3

# hdd.tools.py - performs operations on a Xbox 360 HDD
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

import argparse
import os
import shutil
import struct

from pathlib import Path
from xe import xtaf

parser = argparse.ArgumentParser(
                    prog='HDD Tools',
                    description='performs operations on a Xbox 360 HDD')

parser.add_argument(
    '--drive',
    help='Device file or HDD image',
    type=str,
    required=True
)

parser.add_argument(
    '--lspart',
    help='List Partitions',
    action='store_true',
)

parser.add_argument(
    '--part',
    help='Select Partition to operate on [default DataPartition]',
    type=str,
    default='DataPartition'
)

parser.add_argument(
    '--insert',
    help='Add file or folder to filesystem. Usage --insert /path/to/file',
    type=str,
    default=None
)
parser.add_argument(
    '--insert-contents',
    help='Insert contents of folder to filesystem. Usage --insert /path/to/directory',
    type=str,
    default=None
)

parser.add_argument(
    '--path',
    help='Specify a path other than the root directory to operate on',
    type=str,
    default=None
)

parser.add_argument(
    '--dumpall',
    help='Dumps all partitions to [path]. Usage --dumpall [path]. Warning: Clears target directory. Example --dumpall /path/to/hdd/backup',
    type=str,
    default=None
)

parser.add_argument(
    '--beta-1838',
    '--dev-geometry',
    '-b1838',
    '-dg',
    help='Calculates new/fixed geometry for devkit >= 1838',
    action='store_true'
)
parser.add_argument(
    '--beta-1640',
    '-b1640',
    help='Calculates new/fixed geometry for 1640. Early kernel versions do not respect the partition table.',
    action='store_true'
)
parser.add_argument(
    '--commit-table',
    '-c',
    help='Writes a new devkit partition table to the drive. Use in combination with -b1640 or -dg',
    action='store_true'
)
parser.add_argument(
    '--format-all',
    help='Formats all partitions!!!',
    action='store_true'
)
parser.add_argument(
    '--format',
    help='Formats a specific partition!!!. Example: --format SystemPartition'
)
parser.add_argument(
    '--backup-ss',
    '-bss',
    help='Saves security sector to [file]. Usage: hdd.tools.py -bss [file]'
)
parser.add_argument(
    '--ignore-ss',
    '-iss',
    help='ignores sector count in security sector',
    action='store_true'
)

args = parser.parse_args()

hdd = args.drive

drive = os.fdopen(os.open(hdd, os.O_RDWR), 'rb+')
drive.seek(0, 2)
size = drive.tell()
sectors = size//512

print(f'Opened {hdd} sz {size} [{hex(size)}] sectors {sectors} [{hex(sectors)}]')

print(f'[load_security_sector]')
drive.seek(0x2000)
ss = drive.read(512+4)

serial, firmware, model = struct.unpack_from('20s8s40s', ss, 0)
logo_size = struct.unpack_from('!L', ss, 512)[0]
ss_sectors = struct.unpack_from('<L', ss, 0x58)[0]
try:
    serial = serial.decode()
    firmware = firmware.decode()
    model = model.decode()
except UnicodeError:
    print(f'\tfailed to decode info strings, security sector is likely invalid, ignoring...')
else:
    serial = serial.strip()
    firmware = firmware.strip()
    model = model.strip()
    print(f'\tdrive serial number:\t\t{serial}')
    print(f'\tdrive firmware revision:\t{firmware}')
    print(f'\tdrive model:\t\t\t{model}')
    print(f'\tlogo_size={logo_size}')
    print(f'\tsectors={hex(ss_sectors)}')
    if logo_size < 0x10000:
        ss += drive.read(logo_size)

if args.backup_ss:
    print(f'[backup_security_sector] {args.backup_ss}')
    open(args.backup_ss, 'wb').write(ss)

if sectors > 0xFFFFFFFF:
    print(f'Warning: Drive larger than 2TB, capping sector count')
    sectors = 0xFFFFFFFF
if ss_sectors < sectors:
    sectors = ss_sectors
    print(f'Warning: capping drive capcity to security sector size, {hex(sectors)} sectors. Use --ignore-ss to override.')
size = sectors * 512

partition_table_partitions = [
    'DataPartition',
    'SystemPartition',
    None,
    'DumpPartition',
    'PixDumpPartition',
    None,
    None,
    'AltFlash',
    'Cache0',
    'Cache1'
]

if args.beta_1838 or args.beta_1640:
    beta_partition_table = dict()

    print(f'[calc_devkit_geometry]')

    print(f'\tUser sectors: {hex(sectors)}')

    pixdump_sz = sectors>>3 & 0x1FFFFF80
    pixdump_sz = min(pixdump_sz, 0x1400000)
    print(f'\tPixdump Sz: {hex(pixdump_sz)}')


    beta_partition_table['PixDumpPartition'] = (0x400, pixdump_sz)

    cache_sz = 0x400000
    cache0_start = sectors-cache_sz
    cache1_start = cache0_start-cache_sz
    beta_partition_table['Cache0'] = (cache0_start, cache_sz)
    beta_partition_table['Cache1'] = (cache1_start, cache_sz)

    altflash_sz = 0x20000
    altflash_start = cache1_start - altflash_sz
    beta_partition_table['AltFlash'] = (altflash_start, altflash_sz)

    dump_start = 0x400+pixdump_sz
    dump_sz = 0x107180
    beta_partition_table['DumpPartition'] = (dump_start, dump_sz)

    system_start = dump_start+dump_sz
    if args.beta_1838:
        system_sz = 0x80000
    elif args.beta_1640:
        system_sz = 0x10000
    beta_partition_table['SystemPartition'] = (system_start, system_sz)

    data_start = system_start+system_sz
    data_sz = altflash_start-data_start
    beta_partition_table['DataPartition'] = (data_start, data_sz)

    print(f'[calculated_devkit_table_sectors]')
    keys = sorted(beta_partition_table.keys(), key=lambda x: beta_partition_table[x][0])
    for pn in keys:
        start, sz = beta_partition_table[pn]
        print(f'\t{pn} [{hex(start)}:{hex(start+sz)}] sz {hex(sz)}')

    if args.commit_table:
        print(f'[commit_devkit_partition_table]')
        partition_table = bytearray(0x200)
        struct.pack_into('!L2H', partition_table, 0, 0x20000, 1838, 1)
        for idx, name in enumerate(partition_table_partitions):
            if name:
                struct.pack_into('!2L', partition_table, 8*(idx+1), beta_partition_table[name][0], beta_partition_table[name][1])
        drive.seek(0)
        drive.write(partition_table)
        print(f'\tOK')

    # Convert to byte offsets, TODO: should probably refactor all this code to just use sector offsets
    for pn in keys:
        beta_partition_table[pn] = (beta_partition_table[pn][0]*512, beta_partition_table[pn][1]*512)



# Retail:
# Cache0 @ 0x400
# Cache1 @ 0x400400
# Dump @ 0x800400

# Beta 1838 @ 60GB Example
# User Addressable Sectors:
#   0x6fc7c80
# 0x400:+0xdf8f80 PixDumpPartition [0x400:0xDF9380]
# 0xdf9380:+0x107180 DumpPartition [0xDF9380:0xF00500]
# 0xf00500:+0x80000 SystemPartition [0xf00500:0xf80500]
# 0xf80500:+0x5827780 DataPartition [0xF80500:0x67A7C80]
# 0x67a7c80:+0x20000 AltFlash [0x67a7c80:0x67C7C80]
# 0x67c7c80:+0x400000 Cache1 [0x67c7c80:0x6BC7C80]
# 0x6bc7c80:+0x400000 Cache0 [0x6bc7c80:0x6FC7C80]

# Beta 1640 @ 60GB Example
# PhysicalDisk  [0x0000000000000000, +0x0000000006fc7c80]
# PixDump       [0x0000000000000400, +0x0000000000df8f80]
# Cache0        [0x0000000006bc7c80, +0x0000000000400000]
# Cache1        [0x00000000067c7c80, +0x0000000000400000]
# AltFlash      [0x00000000067a7c80, +0x0000000000020000]
# Dump          [0x0000000000df9380, +0x0000000000107180]
# System        [0x0000000000f00500, +0x0000000000010000]
# DataPartition [0x0000000000f10500, +0x0000000005897780]

retail_partition_table = {
    'DataPartition': (0x130eb0000, size-0x130eb0000),
    'SystemPartition': (0x120eb0000, 0x10000000),
    'SysExt': (0x10C080000, 0xCE30000),
    'SysAux': (0x118EB0000, 0x8000000)
}

drive.seek(0)
table = drive.read(0x200)

mode = 'dev'

if not args.beta_1838 and not args.beta_1640:
    print(f'[devkit_detect]')
    magic, version = struct.unpack_from('!2L', table, 0)
    if magic != 0x20000:
        print(f'Dev HDD Magic Invalid, got {hex(magic)}')
        print(f'falling back to retail')
        mode = 'retail'
    else:
        if version < 0x5F50001:
            print(f'\tdevkit partition table version too early, ignore, falling back to retail')
            mode = 'retail'
        else:
            print(f'\tdetected devkit partition table version {version>>16}.{version&0xFF}')

if args.beta_1838 or args.beta_1640:
    partdict = beta_partition_table
else:
    if mode == 'dev':
        partdict = dict()

        dumpstart = None

        # print(f'[devkit_partition_table]')
        for idx,part in enumerate(partition_table_partitions):
            if part is None:
                continue
            #if part == 'DataPartition':
            #    continue
            offset, length = struct.unpack_from('!2L', table, idx*8+8)
            # print(f'\t{part} @ sector {hex(offset)} for {hex(length)} sectors [@{hex(offset*512)}b]')

            partdict[part] = (offset*512, length*512)

            if part == 'DumpPartition':
                dumpstart = offset

        if partdict['DumpPartition']:
            partdict['SysExt'] = (partdict['DumpPartition'][0])+0xC000000, 0xCE30000
            partdict['SysAux'] = (partdict['DumpPartition'][0])+0x18E30000, 0x8000000

    elif mode == 'retail':
        partdict = retail_partition_table

if args.lspart:
    print(f'[list_partitions]')
    keys = sorted(partdict.keys(), key=lambda x: partdict[x][0])
    for part in keys:
        drive.seek(partdict[part][0])
        magic = drive.read(4)
        exists = magic == b'XTAF'
        print(f'\t{part} [{hex(partdict[part][0])}, {hex(partdict[part][0]+partdict[part][1])}] sz {hex(partdict[part][1])} exists {exists}')

if args.dumpall:
    if os.path.isfile(args.dumpall):
        os.remove(args.dumpall)
    elif os.path.isdir(args.dumpall):
        shutil.rmtree(args.dumpall)
    os.makedirs(args.dumpall)

    for partition in partdict:
        subpath = os.path.join(args.dumpall, partition)

        offset, length = partdict[partition]

        drive.seek(offset)
        if drive.read(4) != b'XTAF':
            print(f'No XTAF magic for {partition}, skipping')
            continue

        if partition in ['PixDumpPartition', 'DumpPartition', 'Cache0', 'Cache1']:
            print(f'skipping {partition} (not xtaf)')
            continue

        print(f'Dumping {partition} @ {hex(offset)}:+{hex(length)}')
        os.mkdir(subpath)

        xta = xtaf.XTAFPartition(drive, partition_offset=offset, partition_sz=length)
        xta.unpack(subpath)

if args.format_all:
    print(f'[Formatting all partitions!!!]')
    for partition in partdict:
        offset, length = partdict[partition]
        if partition in ['SystemPartition', 'DataPartition', 'AltFlash', 'SysExt', 'SysAux']:
            xta = xtaf.XTAFPartition(drive, partition_offset=offset, partition_sz=length, initialize=True)

if args.format:
    print(f'[Formatting: {args.format}]')
    offset, length = partdict[args.format]
    xta = xtaf.XTAFPartition(drive, partition_offset=offset, partition_sz=length, initialize=True)

if args.insert:
    if args.part not in partdict:
        print(f'insert: unknown partition {args.part}')
        exit(-1)
    offset, length = partdict[args.part]

    print(f'Operating on {args.part}')

    xta = xtaf.XTAFPartition(drive, partition_offset=offset, partition_sz=length)

    if args.insert:
        dir_offset = xta.root_offset

        if os.path.isdir(args.insert):
            print(f'Importing folder {args.insert}')
            xta.import_folder_to_dir(dir_offset, args.insert)

if args.insert_contents:
    if not os.path.isdir(args.insert_contents):
        print(f'not a directory: {args.insert_contents}')
        exit(-1)

    if args.part not in partdict:
        print(f'insert: unknown partition {args.part}')
        exit(-1)
    offset, length = partdict[args.part]

    print(f'Operating on {args.part}')
    xta = xtaf.XTAFPartition(drive, partition_offset=offset, partition_sz=length)

    dir_offset = xta.root_offset

    for entry in os.scandir(args.insert_contents):
        if entry.is_file():
            print('file:', entry.path)
            xta.add_file_to_dir(dir_offset, entry.path)
        elif entry.is_dir():
            print('dir:', entry.path)
            xta.import_folder_to_dir(dir_offset, entry.path)
