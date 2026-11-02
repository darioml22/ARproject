using UnityEngine;

namespace Xrqr.Unity
{
    /// <summary>
    /// Decodes a metadata thumbnail, a WebP image of at most 512 pixels on its longer side
    /// (section 6.1), with libwebp through unity.webp, since <c>Texture2D.LoadImage</c> reads only
    /// PNG and JPEG.
    /// </summary>
    public static class Thumbnail
    {
        /// <summary>The longer side the specification allows, in pixels.</summary>
        public const int MaxSide = 512;

        /// <summary>Returns the decoded texture, or <c>null</c> for an image that does not decode or is too large.</summary>
        public static Texture2D Decode(byte[] webp)
        {
            var texture = WebP.Texture2DExt.CreateTexture2DFromWebP(webp, false, false, out var error);
            if (error != WebP.Error.Success || texture == null)
            {
                Debug.LogWarning($"XRQR: thumbnail not decoded ({error})");
                return null;
            }
            if (Mathf.Max(texture.width, texture.height) > MaxSide)
            {
                Debug.LogWarning($"XRQR: thumbnail of {texture.width}x{texture.height} exceeds {MaxSide} pixels");
                Object.Destroy(texture);
                return null;
            }
            return texture;
        }
    }
}
