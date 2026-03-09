using UnityEngine;

/// <summary>
/// Manages package collection logic: detects pickup within a radius,
/// then respawns the package at a random XZ position within a configurable ring.
/// </summary>
public class PackageCollectionManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The car (or agent) that collects packages.")]
    [SerializeField] private Transform agent;

    [Tooltip("The package target transform (same one assigned to LmStudioActuatorTicker.packageTarget).")]
    [SerializeField] private Transform package;

    [Header("Collection")]
    [Tooltip("Distance in meters at which the package counts as collected.")]
    [SerializeField] private float collectionRadius = 10f;

    [Header("Spawn Range")]
    [Tooltip("Minimum distance from the agent for the next package spawn.")]
    [SerializeField] private float minSpawnRadius = 30f;

    [Tooltip("Maximum distance from the agent for the next package spawn.")]
    [SerializeField] private float maxSpawnRadius = 100f;

    [Tooltip("Fixed Y height for spawned packages.")]
    [SerializeField] private float spawnY = 0f;

    [Header("Stats")]
    [SerializeField] private int packagesCollected;

    public int PackagesCollected => packagesCollected;

    private void Update()
    {
        if (agent == null || package == null)
            return;

        // 2D distance check (XZ plane only)
        Vector3 diff = package.position - agent.position;
        diff.y = 0f;
        float dist = diff.magnitude;

        if (dist <= collectionRadius)
        {
            packagesCollected++;
            Debug.Log($"Package collected! Total: {packagesCollected}");
            RespawnPackage();
        }
    }

    private void RespawnPackage()
    {
        // Random direction on XZ plane
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(minSpawnRadius, maxSpawnRadius);

        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        Vector3 newPos = agent.position + offset;
        newPos.y = spawnY;

        package.position = newPos;
    }
}
