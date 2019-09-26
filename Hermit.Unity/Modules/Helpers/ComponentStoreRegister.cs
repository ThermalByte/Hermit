using UnityEngine;

namespace Hermit.Unity
{
    [ScriptOrder(-5000)]
    public sealed class ComponentStoreRegister : MonoBehaviour
    {
        [SerializeField]
        private string _storeId = "Global";

        [SerializeField]
        private string _storeKey = "";

        [SerializeField]
        private Component _storeValue = null;

        private void Start()
        {
            if (_storeValue == null || string.IsNullOrEmpty(_storeId) || string.IsNullOrEmpty(_storeKey)) { return; }

            var store = Her.GetStore(_storeId);
            store.Set(_storeKey, _storeValue);
        }
    }
}