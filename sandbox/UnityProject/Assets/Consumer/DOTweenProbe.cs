using DG.Tweening;
using UnityEngine;

// Deliberate violations of the DOTween-conditional rules (UPA2030-2032). The Consumer
// assembly references the DOTweenStub asmdef, whose assembly is named exactly "DOTween",
// so UpaProfile.HasDOTween is true here and these rules must register.
public class DOTweenProbe : MonoBehaviour
{
    Vector3 _target;

    void Update()
    {
        transform.DOMove(_target, 1f);                            // UPA2030
    }

    void Start()
    {
        transform.DORotate(_target, 1f).SetLoops(-1);             // UPA2031
        transform.DOMove(_target, 1f).SetId("walk");              // UPA2032
    }

    void OnDisable()
    {
        DG.Tweening.DOTween.Kill("walk");                         // UPA2032
    }
}
