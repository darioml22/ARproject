using System;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Xrqr.Unity
{
    /// <summary>A model the client loaded: an instantiated scene kept as a template to copy.</summary>
    public sealed class LoadedModel : IDisposable
    {
        public GameObject Template { get; }
        readonly GltfImport m_Import;

        internal LoadedModel(GameObject template, GltfImport import)
        {
            Template = template;
            m_Import = import;
        }

        /// <summary>Destroys the template and releases the meshes, materials, and textures every copy shares.</summary>
        public void Dispose()
        {
            if (Template != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(Template);
                else
                    Object.DestroyImmediate(Template);
            }
            m_Import.Dispose();
        }
    }

    /// <summary>A verified model that glTFast could not load, such as one requiring an extension outside its set.</summary>
    public sealed class ModelLoadException : Exception
    {
        public ModelLoadException(string message) : base(message) { }
    }

    /// <summary>
    /// Loads verified GLB octets with glTFast from memory (DR-23), so that only bytes the client
    /// fetched and checked reach the loader. glTFast's <c>Load</c> also reads glTF JSON, so the GLB
    /// header is checked first.
    /// </summary>
    public static class ModelLoader
    {
        /// <summary>
        /// Loads <paramref name="glb"/> and instantiates its default scene under a new object
        /// parented to <paramref name="holder"/>, which should be inactive so the template stays
        /// out of view.
        /// </summary>
        public static async Task<LoadedModel> LoadAsync(byte[] glb, string name, Transform holder, CancellationToken cancellation)
        {
            if (!IsGlb(glb))
                throw new ModelLoadException("the model is not a GLB of container version 2");
            // glTFast's default agent spreads work over frames from an object kept with
            // DontDestroyOnLoad, which exists only in play mode.
            var import = new GltfImport(deferAgent: Application.isPlaying ? null : new UninterruptedDeferAgent());
            try
            {
                if (!await import.Load(glb, null, null, cancellation))
                    throw new ModelLoadException("glTFast could not read the model");
                var root = new GameObject(name);
                root.transform.SetParent(holder, false);
                if (!await import.InstantiateMainSceneAsync(root.transform, cancellation))
                {
                    Object.Destroy(root);
                    throw new ModelLoadException("glTFast could not instantiate the model's scene");
                }
                return new LoadedModel(root, import);
            }
            catch
            {
                import.Dispose();
                throw;
            }
        }

        /// <summary>Tells whether <paramref name="data"/> starts with the GLB header: the magic <c>glTF</c> and container version 2 (section 8).</summary>
        static bool IsGlb(byte[] data) =>
            data.Length >= 12 && data[0] == (byte)'g' && data[1] == (byte)'l' && data[2] == (byte)'T' && data[3] == (byte)'F'
            && data[4] == 2 && data[5] == 0 && data[6] == 0 && data[7] == 0;
    }
}
