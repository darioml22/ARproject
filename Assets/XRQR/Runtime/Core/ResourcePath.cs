namespace Xrqr
{
    /// <summary>Which of the two resources of an id a path names.</summary>
    public enum ResourceKind
    {
        /// <summary>The served model, <c>model/gltf-binary</c>.</summary>
        Model,
        /// <summary>The metadata document, <c>application/json</c>.</summary>
        Meta,
    }

    /// <summary>
    /// A client resource (section 5.1): an id, an optional pinned version, and the kind. Its
    /// text form is the canonical path relative to a resolver base.
    /// </summary>
    public readonly struct ResourcePath
    {
        public XrqrId Id { get; }
        /// <summary>The pinned version, or <c>null</c> for the latest.</summary>
        public uint? Version { get; }
        public ResourceKind Kind { get; }

        public ResourcePath(XrqrId id, uint? version, ResourceKind kind)
        {
            Id = id;
            Version = version;
            Kind = kind;
        }

        public static ResourcePath Model(XrqrId id, uint? version = null) => new ResourcePath(id, version, ResourceKind.Model);
        public static ResourcePath Meta(XrqrId id, uint? version = null) => new ResourcePath(id, version, ResourceKind.Meta);

        /// <summary>Writes the canonical path, such as <c>/{id}/v/3/meta</c>.</summary>
        public override string ToString()
        {
            var path = "/" + Id.Text;
            if (Version.HasValue)
                path += "/v/" + Version.Value;
            return Kind == ResourceKind.Meta ? path + "/meta" : path;
        }

        /// <summary>
        /// Reads a path relative to a resolver base. Error kinds: <c>shape</c>, <c>id</c>,
        /// <c>version</c>, <c>version-range</c>.
        /// </summary>
        public static ResourcePath Parse(string path)
        {
            if (path == null || !path.StartsWith("/", System.StringComparison.Ordinal))
                throw Shape();
            var segments = path.Substring(1).Split('/');
            string id, version = null;
            ResourceKind kind;
            if (segments.Length == 1)
            {
                id = segments[0];
                kind = ResourceKind.Model;
            }
            else if (segments.Length == 2 && segments[1] == "meta")
            {
                id = segments[0];
                kind = ResourceKind.Meta;
            }
            else if (segments.Length == 3 && segments[1] == "v")
            {
                id = segments[0];
                version = segments[2];
                kind = ResourceKind.Model;
            }
            else if (segments.Length == 4 && segments[1] == "v" && segments[3] == "meta")
            {
                id = segments[0];
                version = segments[2];
                kind = ResourceKind.Meta;
            }
            else
            {
                throw Shape();
            }
            if (id.Length == 0)
                throw Shape();
            XrqrId parsed;
            try
            {
                parsed = XrqrId.Decode(id);
            }
            catch (XrqrException e)
            {
                throw new XrqrException(Stage.Path, "id", e.Message);
            }
            return new ResourcePath(parsed, version == null ? (uint?)null : ParseVersion(version), kind);
        }

        static uint ParseVersion(string text)
        {
            if (text.Length == 0 || text[0] == '0')
                throw new XrqrException(Stage.Path, "version", "a version is a decimal integer of at least 1 without leading zeros");
            ulong value = 0;
            foreach (var c in text)
            {
                if (c < '0' || c > '9')
                    throw new XrqrException(Stage.Path, "version", "a version is a decimal integer of at least 1 without leading zeros");
                if (value <= uint.MaxValue)
                    value = value * 10 + (ulong)(c - '0');
            }
            if (value > uint.MaxValue)
                throw new XrqrException(Stage.Path, "version-range", "a version is at most 2^32 - 1");
            return (uint)value;
        }

        static XrqrException Shape() => new XrqrException(Stage.Path, "shape", "a path is one of the four resource paths");
    }
}
