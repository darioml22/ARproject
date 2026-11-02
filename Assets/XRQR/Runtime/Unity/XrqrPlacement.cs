using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Samples.ARStarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Xrqr.Unity
{
    /// <summary>
    /// Places a loaded model through the template's spawn chain. The spawn trigger's scene
    /// listeners are switched off and its taps come here: with no model armed they go to the
    /// object spawner as before; with a model armed, the surface hint (section 6.1, DR-08)
    /// decides which planes take it, and the model is spawned at real scale.
    /// </summary>
    public sealed class XrqrPlacement
    {
        /// <summary>The cosine of the largest tilt of a plane that still counts as a floor.</summary>
        const float FloorCosine = 0.9f;
        /// <summary>The cosine of the smallest tilt of a plane that still counts as a wall.</summary>
        const float WallCosine = 0.25f;

        readonly ObjectSpawner m_Spawner;
        LoadedModel m_Model;
        Surface m_Surface;
        Vector3 m_LastNormal;

        /// <summary>Reports a tap that the surface hint refused, with the reason.</summary>
        public event System.Action<string> Refused;
        /// <summary>Reports a placed copy of the armed model.</summary>
        public event System.Action<GameObject> Placed;

        public bool IsArmed => m_Model != null;

        public XrqrPlacement(ARInteractorSpawnTrigger trigger, ObjectSpawner spawner)
        {
            m_Spawner = spawner;
            var spawnEvent = trigger.objectSpawnTriggered;
            for (var i = 0; i < spawnEvent.GetPersistentEventCount(); i++)
                spawnEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
            spawnEvent.AddListener(OnSpawnRequested);
            spawner.objectSpawned += OnObjectSpawned;
        }

        /// <summary>Makes taps place <paramref name="model"/> on surfaces that <paramref name="surface"/> allows.</summary>
        public void Arm(LoadedModel model, Surface surface)
        {
            m_Model = model;
            m_Surface = surface;
        }

        public void Disarm() => m_Model = null;

        void OnSpawnRequested(Vector3 position, Vector3 normal)
        {
            if (m_Model == null)
            {
                m_Spawner.TrySpawnObject(position, normal);
                return;
            }
            var up = Vector3.Dot(normal.normalized, Vector3.up);
            if (m_Surface == Surface.Floor && up < FloorCosine)
            {
                Refused?.Invoke("This model stands on a floor: tap a horizontal surface.");
                return;
            }
            if (m_Surface == Surface.Wall && Mathf.Abs(up) > WallCosine)
            {
                Refused?.Invoke("This model hangs on a wall: tap a vertical surface.");
                return;
            }

            // The template joins the spawner's list only for this spawn, so the template menu's
            // random choice never picks it.
            var prefabs = m_Spawner.objectPrefabs;
            prefabs.Add(m_Model.Template);
            var previousIndex = m_Spawner.spawnOptionIndex;
            var previousRandomAngle = m_Spawner.applyRandomAngleAtSpawn;
            m_Spawner.spawnOptionIndex = prefabs.Count - 1;
            m_Spawner.applyRandomAngleAtSpawn = false;
            m_LastNormal = normal;
            try
            {
                m_Spawner.TrySpawnObject(position, normal);
            }
            finally
            {
                prefabs.RemoveAt(prefabs.Count - 1);
                m_Spawner.spawnOptionIndex = previousIndex;
                m_Spawner.applyRandomAngleAtSpawn = previousRandomAngle;
            }
        }

        void OnObjectSpawned(GameObject spawned)
        {
            if (m_Model == null || spawned.name != m_Model.Template.name + "(Clone)")
                return;
            spawned.SetActive(true);
            // A wall model's back faces the wall: glTF +Z is the front, and +Y stays up.
            if (m_Surface == Surface.Wall)
                spawned.transform.rotation = Quaternion.LookRotation(m_LastNormal, Vector3.up);
            Placed?.Invoke(spawned);
        }
    }
}
