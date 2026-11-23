
using UnityEngine;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    public sealed class TechTreeNodeView : MonoBehaviour
    {
        [SerializeField] private Image border;
        [SerializeField] private Image background;
        [SerializeField] private Text classification;
        [SerializeField] private Text vehicleName;

        public Image Border => border;
        public Image Background => background;
        public Text Classification => classification;
        public Text VehicleName => vehicleName;
    }
}
