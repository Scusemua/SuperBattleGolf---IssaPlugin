using UnityEngine;

namespace IssaPlugin
{
    /// Fades a Light's intensity from its initial value to zero over a given
    /// duration, then destroys the GameObject it is attached to.
    ///
    /// Added at runtime via AddComponent — the class lives in the mod DLL so it
    /// does not need to be present in the asset bundle.
    public class LightFader : MonoBehaviour
    {
        public enum OnLifeEnd
        {
            DoNothing,
            Disable,
            Destroy,
        }

        [Header("Seconds to dim the light")]
        public float life = 0.5f;
        public OnLifeEnd onLifeEnd = OnLifeEnd.Destroy;

        private Light li;
        private float initIntensity;

        // Use this for initialization
        private void Start()
        {
            li = GetComponent<Light>();
            if (li != null)
            {
                initIntensity = li.intensity;
            }
        }

        // Update is called once per frame
        private void Update()
        {
            if (li != null)
            {
                li.intensity -= initIntensity * (Time.deltaTime / life);
                if (li.intensity <= 0f)
                {
                    switch (onLifeEnd)
                    {
                        case OnLifeEnd.DoNothing:
                            // Do nothing
                            break;
                        case OnLifeEnd.Disable:
                            li.enabled = false;
                            break;
                        case OnLifeEnd.Destroy:
                            Destroy(li);
                            break;
                    }
                }
            }
        }
    }
}
