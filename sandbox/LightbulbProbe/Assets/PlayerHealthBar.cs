using UnityEngine;

// Screenshot material. Not part of the eleven-span code-fix walkthrough in README.md --
// this file exists so the README's first screen can show the rules firing on code that
// looks like a game rather than on a probe that names the rules it is testing.
//
// Everything below is a deliberate violation. Do not copy it.
//
// Two things are avoided on purpose, because both would put noise in the screenshot:
// Rigidbody.velocity, which Unity 6 marks obsolete and would add a deprecation warning;
// and unassigned serialized fields, which the compiler reports as CS0649 in a project
// without Microsoft.Unity.Analyzers and its suppressors.
public class PlayerHealthBar : MonoBehaviour
{
  [SerializeField]
  private Renderer barRenderer = null;

  [SerializeField]
  private Transform target = null;

  private float health = 1f;

  private void Update()
  {
    if (Vector3.Distance(transform.position, target.position) > 20f)
    {
      return;
    }

    var player = GameObject.Find("Player");
    var playerRenderer = player.GetComponent<Renderer>();

    health = Mathf.Clamp01(playerRenderer.bounds.size.y / 10f);
    barRenderer.material.SetFloat("_Fill", health);

    transform.LookAt(Camera.main.transform);
  }
}
