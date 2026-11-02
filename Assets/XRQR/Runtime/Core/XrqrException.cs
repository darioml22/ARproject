using System;

namespace Xrqr
{
    /// <summary>The step of the protocol a rejection belongs to.</summary>
    public enum Stage
    {
        /// <summary>The text of a QR symbol (specification section 2).</summary>
        Code,
        /// <summary>An id outside a code (section 3).</summary>
        Id,
        /// <summary>A resource path (section 5.1).</summary>
        Path,
        /// <summary>A resolver entry (section 4.1).</summary>
        Entry,
        /// <summary>A metadata response (sections 6 and 7.2).</summary>
        Meta,
        /// <summary>A model response (section 7.3).</summary>
        Model,
    }

    /// <summary>
    /// A rejection by the protocol core. <see cref="Kind"/> is the error kind as the conformance
    /// vectors spell it, such as <c>signature</c> or <c>first-symbol</c>.
    /// </summary>
    public sealed class XrqrException : Exception
    {
        public Stage Stage { get; }
        public string Kind { get; }

        public XrqrException(Stage stage, string kind, string message)
            : base(message)
        {
            Stage = stage;
            Kind = kind;
        }

        /// <summary>Returns the same rejection under another stage, keeping its kind.</summary>
        internal XrqrException At(Stage stage) => new XrqrException(stage, Kind, Message);
    }
}
