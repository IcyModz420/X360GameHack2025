// =============================================================================
// XDCKIT.XboxConsole.Screenshot.cs - one-call screenshot saving
// =============================================================================
// Adds `Screenshot(string path, …)` overloads alongside
// <see cref="Screenshot(out byte[])"/> / <see cref="Screenshot(out byte[], string)"/>
// — detile + PNG-encode in one call.  Raw helpers stay in the Features partial.
// =============================================================================
using System;
using System.IO;

    public partial class XboxConsole
    {
        /// <summary>
        /// Capture the front buffer and save it as PNG at <paramref name="path"/>.
        /// Returns the parsed metadata for callers that want to display
        /// width/height/etc.  Falls back to writing the raw bytes beside the
        /// requested path with a <c>.bin</c> extension when the surface isn't a
        /// 32 bpp ARGB front buffer (returns <c>false</c> via <paramref name="savedPng"/>).
        /// </summary>
        /// <param name="path">Destination <c>.png</c> path.</param>
        /// <param name="savedPng">
        /// <c>true</c> when a PNG was written to <paramref name="path"/>;
        /// <c>false</c> when geometry couldn't be decoded and a sibling <c>.bin</c>
        /// was written instead.
        /// </param>
        /// <param name="savedPath">Actual file written (PNG or fallback BIN).</param>
        public ScreenshotInfo Screenshot(string path, out bool savedPng, out string savedPath)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path required.", nameof(path));

            var info = Screenshot(out byte[] frame);

            if (frame == null || frame.Length == 0)
            {
                savedPng = false;
                savedPath = null;
                return info;
            }

            bool wantRaw = string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase);

            if (!wantRaw && XboxScreenshot.TryEncodePng(info, frame, path))
            {
                savedPng = true;
                savedPath = path;
                return info;
            }

            string rawPath = wantRaw ? path : Path.ChangeExtension(path, ".bin");
            System.IO.File.WriteAllBytes(rawPath, frame);
            savedPng = false;
            savedPath = rawPath;
            return info;
        }

        /// <summary>
        /// Capture and save a PNG screenshot at <paramref name="path"/>.
        /// Returns <c>true</c> if a PNG was written, <c>false</c> if the
        /// surface couldn't be decoded and a <c>.bin</c> fallback was used.
        /// </summary>
        public bool Screenshot(string path)
        {
            Screenshot(path, out bool savedPng, out _);
            return savedPng;
        }
    }
