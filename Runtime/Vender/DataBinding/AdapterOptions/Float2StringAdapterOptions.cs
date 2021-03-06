using UnityEngine;

namespace Hermit.DataBinding
{
    [CreateAssetMenu(menuName = "Hermit/Adapter Options/Float => String")]
    public class Float2StringAdapterOptions : AdapterOptions
    {
        [Header("Options")]
        public string format = "{0}";

        public override object Convert(object fromObj)
        {
            return string.Format(format, fromObj);
        }
    }
}