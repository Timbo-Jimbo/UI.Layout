using UnityEngine;

namespace TimboJimbo.UI.Layout.Samples.ViewTransitions
{
    /// <summary>Turns its first child forever: running state that shows a persisting object is the same object, never restarted.</summary>
    public sealed class Spinner : MonoBehaviour
    {
        public float DegreesPerSecond = 90f;

        private void Update()
        {
            if (transform.childCount > 0)
                transform.GetChild(0).Rotate(0f, 0f, -DegreesPerSecond * Time.deltaTime);
        }
    }
}
