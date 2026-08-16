// =============================================================================
// XDCKIT.XboxConsole.Users.cs - Devkit user accounts + console security
// =============================================================================
// Wraps the xbdm exports that manage who is allowed to attach to the kit.
// Wire formats are taken straight from the disassembled Microsoft xbdm.dll
// (XBDM.asm / aUserNameS / aUserNameSRemov / aUserlist / aGetuserprivMe /
// aGetuserprivNam / aSetuserprivNam / aAdminpwPasswd0 / aAdminpwNone /
// aLockmodeBoxid0 / aLockmodeUnlock / aAuthuserNameSP / aAuthuserNameSR).
//
// These commands only ever do anything meaningful on real Microsoft devkits
// that have ACL/security enabled; on a JTAG/RGH running freebooted xbdm
// they're harmless no-ops (the natelx open-source clone in xbdm-master/
// doesn't dispatch them at all).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

    public partial class XboxConsole
    {
        #region User accounts (DmAddUser / DmRemoveUser / DmWalkUserList)

        /// <summary>DmAddUser — wraps SDK <c>USER NAME="%s"</c>.</summary>
        public void AddUser(string name) => SendTextCommand("user name=" + XboxExtensions.QuoteXbdm(name));

        /// <summary>DmRemoveUser — wraps SDK <c>USER NAME="%s" REMOVE</c>.</summary>
        public void RemoveUser(string name)
            => SendTextCommand("user name=" + XboxExtensions.QuoteXbdm(name) + " remove");

        /// <summary>
        /// DmWalkUserList / DmCloseUserList — wraps SDK <c>USERLIST</c>.
        /// Each row in the 202 body is something like
        /// <c>name="..." access=0xN [admin]</c>.
        /// </summary>
        public List<XboxUser> WalkUserList()
        {
            var list = new List<XboxUser>();
            var resp = SendTextCommand("userlist");
            if ((int)resp.Status != 202) return list;

            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new XboxUser
                {
                    Name        = ParseKvLine(line, "name"),
                    AccessFlags = (XboxAccessFlags)ParseUIntKvHex(line, "access"),
                    IsAdmin     = line.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0,
                });
            }
            return list;
        }

        #endregion

        #region User privileges (DmGetUserPriv / DmSetUserPriv)

        /// <summary>DmGetUserPriv (current connection) — wraps SDK <c>GETUSERPRIV ME</c>.</summary>
        public XboxAccessFlags GetMyUserPriv()
        {
            var resp = SendTextCommand("getuserpriv me");
            if (!resp.IsSuccess) return 0;
            return (XboxAccessFlags)ParseUIntKvHex(resp.StatusMessage, "access");
        }

        /// <summary>DmGetUserPriv — wraps SDK <c>GETUSERPRIV NAME="%s"</c>.</summary>
        public XboxAccessFlags GetUserPriv(string name)
        {
            var resp = SendTextCommand("getuserpriv name=" + XboxExtensions.QuoteXbdm(name));
            if (!resp.IsSuccess) return 0;
            return (XboxAccessFlags)ParseUIntKvHex(resp.StatusMessage, "access");
        }

        /// <summary>
        /// DmSetUserPriv — wraps SDK <c>SETUSERPRIV NAME="%s"</c>.  We pass
        /// the access mask as <c>access=0xN</c> on the same line; xbdm will
        /// only update the bits that were set in <paramref name="flags"/>.
        /// </summary>
        public void SetUserPriv(string name, XboxAccessFlags flags)
            => SendTextCommand("setuserpriv name=" + XboxExtensions.QuoteXbdm(name) +
                               " access=0x" + ((uint)flags).ToString("X"));

        #endregion

        #region Admin password / lockmode (DmAdminPwd / DmLockMode)

        /// <summary>
        /// DmAdminPwd — wraps SDK <c>ADMINPW PASSWD=0q%08x%08x</c>.  The
        /// <c>0q</c> form is xbdm's qword literal: high then low 32-bit
        /// halves of the 64-bit password.
        /// </summary>
        public void SetAdminPassword(ulong password)
        {
            uint hi = (uint)(password >> 32), lo = (uint)password;
            SendTextCommand($"adminpw passwd=0q{hi:X8}{lo:X8}");
        }

        /// <summary>DmAdminPwd (clear) — wraps SDK <c>ADMINPW NONE</c>.</summary>
        public void ClearAdminPassword() => SendTextCommand("adminpw none");

        /// <summary>
        /// DmLockMode (lock) — wraps SDK <c>LOCKMODE BOXID=0q%08x%08x</c>.
        /// Locks the box to the given <paramref name="boxId"/>.
        /// </summary>
        public void LockToBoxId(ulong boxId)
        {
            uint hi = (uint)(boxId >> 32), lo = (uint)boxId;
            SendTextCommand($"lockmode boxid=0q{hi:X8}{lo:X8}");
        }

        /// <summary>DmLockMode (unlock) — wraps SDK <c>LOCKMODE UNLOCK</c>.</summary>
        public void Unlock() => SendTextCommand("lockmode unlock");

        #endregion

        #region Authentication (DmAuthUser)

        /// <summary>DmAuthUser with password — wraps SDK <c>AUTHUSER NAME="%s" PASSWD=0q%08x%08x</c>.</summary>
        public bool AuthUserWithPassword(string name, ulong password)
        {
            uint hi = (uint)(password >> 32), lo = (uint)password;
            var resp = SendTextCommand("authuser name=" + XboxExtensions.QuoteXbdm(name) +
                                       " passwd=0q" + hi.ToString("X8") + lo.ToString("X8"));
            return resp != null && resp.IsSuccess;
        }

        /// <summary>DmAuthUser with response — wraps SDK <c>AUTHUSER NAME="%s" RESP=0q%08x%08x</c>.</summary>
        public bool AuthUserWithResponse(string name, ulong response)
        {
            uint hi = (uint)(response >> 32), lo = (uint)response;
            var resp = SendTextCommand("authuser name=" + XboxExtensions.QuoteXbdm(name) +
                                       " resp=0q" + hi.ToString("X8") + lo.ToString("X8"));
            return resp != null && resp.IsSuccess;
        }

        /// <summary>DmAuthUser admin response — wraps SDK <c>AUTHUSER ADMIN RESP=0q%08x%08x</c>.</summary>
        public bool AuthAdminWithResponse(ulong response)
        {
            uint hi = (uint)(response >> 32), lo = (uint)response;
            var resp = SendTextCommand($"authuser admin resp=0q{hi:X8}{lo:X8}");
            return resp != null && resp.IsSuccess;
        }

        /// <summary>DmKeyExchange — wraps SDK <c>KEYXCHG</c>; returns the raw 16-byte server nonce / key.</summary>
        public string KeyExchange()
        {
            var resp = SendTextCommand("keyxchg");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        /// <summary>DmGetNonce — wraps SDK <c>NONCE</c>; returns the per-connection nonce.</summary>
        public string GetNonce()
        {
            var resp = SendTextCommand("nonce");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        #endregion
    }
