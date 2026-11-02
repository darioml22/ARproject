using System.Numerics;
using BcEd25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace Xrqr
{
    /// <summary>
    /// Strict Ed25519 verification (section 7.1): a scalar S not below the group order, and a
    /// public key or signature point R of small order, are rejected. BouncyCastle checks S, the
    /// encodings, and the equation; the small-order test of both points is made here, on the
    /// decoded point, so that it does not depend on a list of encodings.
    /// </summary>
    public static class Ed25519Strict
    {
        static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        static readonly BigInteger D = Mod(-121665 * Inverse(121666));
        static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);

        /// <summary>Verifies <paramref name="signature"/> (64 octets) over <paramref name="message"/> with <paramref name="publicKey"/> (32 octets).</summary>
        public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32 || signature == null || signature.Length != 64)
                return false;
            var r = new byte[32];
            System.Array.Copy(signature, r, 32);
            if (IsSmallOrder(publicKey) || IsSmallOrder(r))
                return false;
            return BcEd25519.Verify(signature, 0, publicKey, 0, message, 0, message.Length);
        }

        /// <summary>
        /// Tells whether <paramref name="encoded"/> is a usable public key: it decodes to a curve
        /// point, and that point is not of small order.
        /// </summary>
        public static bool IsValidPublicKey(byte[] encoded) =>
            encoded != null && encoded.Length == 32 && Decompress(encoded, out _, out _) && !IsSmallOrder(encoded);

        /// <summary>
        /// Tells whether <paramref name="encoded"/> decodes to a point whose order divides 8. An
        /// encoding that decodes to no point answers <c>false</c>; the verifier refuses it anyway.
        /// </summary>
        public static bool IsSmallOrder(byte[] encoded)
        {
            if (!Decompress(encoded, out var x, out var y))
                return false;
            for (var i = 0; i < 3; i++)
                Add(x, y, x, y, out x, out y);
            return x.IsZero && y.IsOne;
        }

        /// <summary>
        /// Decodes a point as RFC 8032 section 5.1.3 does, reducing a y coordinate at or above p
        /// as the reference implementation does.
        /// </summary>
        static bool Decompress(byte[] encoded, out BigInteger x, out BigInteger y)
        {
            var bytes = new byte[33];
            System.Array.Copy(encoded, bytes, 32);
            var sign = (bytes[31] >> 7) & 1;
            bytes[31] &= 0x7F;
            y = Mod(new BigInteger(bytes));
            var y2 = y * y % P;
            var u = Mod(y2 - 1);
            var v = Mod(D * y2 + 1);
            var v3 = v * v % P * v % P;
            var v7 = v3 * v3 % P * v % P;
            x = u * v3 % P * BigInteger.ModPow(u * v7 % P, (P - 5) / 8, P) % P;
            var vx2 = v * x % P * x % P;
            if (vx2 != u)
            {
                if (vx2 != Mod(-u))
                    return false;
                x = x * SqrtMinusOne % P;
            }
            if ((int)(x % 2) != sign)
                x = Mod(-x);
            return true;
        }

        /// <summary>Adds two points with the complete twisted Edwards formula for a = -1.</summary>
        static void Add(BigInteger x1, BigInteger y1, BigInteger x2, BigInteger y2, out BigInteger x3, out BigInteger y3)
        {
            var t = D * x1 % P * x2 % P * y1 % P * y2 % P;
            x3 = Mod((x1 * y2 + y1 * x2) % P * Inverse(Mod(1 + t)));
            y3 = Mod((y1 * y2 + x1 * x2) % P * Inverse(Mod(1 - t)));
        }

        static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

        static BigInteger Mod(BigInteger value)
        {
            var r = value % P;
            return r.Sign < 0 ? r + P : r;
        }
    }
}
