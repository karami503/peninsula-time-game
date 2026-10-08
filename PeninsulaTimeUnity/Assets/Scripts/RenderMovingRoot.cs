using UnityEngine;
namespace PeninsulaTime {
    // Vehicles can be parked before their movement controller is attached.
    // Keep their geometry and children out of static world mesh batches.
    public sealed class RenderMovingRoot : MonoBehaviour { }
}
