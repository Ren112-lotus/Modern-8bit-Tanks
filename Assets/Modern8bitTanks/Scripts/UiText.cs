using System;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace Modern8bitTanks
{
    internal static class UiText
    {
        private static StringTable _table;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => _table = null;

        internal static string Get(string source) =>
            string.IsNullOrEmpty(source) ? source : _table?.GetEntry(source)?.Value ?? source;

        internal static string Format(string source, params object[] arguments) =>
            string.Format(Get(source), arguments);

        // Load once per scene entry without blocking the Web player on an Addressables operation.
        internal static void Prepare(Canvas canvas, Action refresh)
        {
            var request = LocalizationSettings.StringDatabase.GetTableAsync("Game UI");
            if (request.IsDone)
                Apply(request);
            else
                request.Completed += Apply;

            void Apply(AsyncOperationHandle<StringTable> operation)
            {
                if (canvas == null || operation.Status != AsyncOperationStatus.Succeeded) return;

                _table = operation.Result;
                foreach (var label in canvas.GetComponentsInChildren<Text>(true))
                    label.text = Get(label.text);
                refresh();
            }
        }
    }
}
