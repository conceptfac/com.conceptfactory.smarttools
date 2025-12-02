#if UNITY_EDITOR
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Concept.SmartTools
{

    [InitializeOnLoad]
public static class SmartTools
{
        private const string TOOLBAR_EXTENDER_NAME = "com.marijnzwemmer.unity-toolbar-extender";
        private const string TOOLBAR_EXTENDER_URL = "https://github.com/marijnz/unity-toolbar-extender.git";

        static SmartTools()
        {
            Debug.LogWarning("[SmartTools] Initialized.");

            Initialize();
        }

        private static async void Initialize()
        {
            bool isInstalled = await IsPackageInstalledAsync(TOOLBAR_EXTENDER_NAME);

            if (isInstalled) return;

            isInstalled = await InstallPackageAsync(TOOLBAR_EXTENDER_URL);

        }


        public static PackageInfo GetPackageInfo(Type type)
        {
            return PackageInfo.FindForAssembly(type.Assembly);

        }


        public static void IsPackageInstalled(string packageName, Action<bool> callback)
        {
            ListRequest request = Client.List();

            void Update()
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= Update;

                if (request.Status == StatusCode.Failure)
                {
                    UnityEngine.Debug.LogError($"Erro ao listar pacotes: {request.Error.message}");
                    callback?.Invoke(false);
                    return;
                }

                bool found = false;
                foreach (var p in request.Result)
                {
                    if (p.name == packageName)
                    {
                        found = true;
                        break;
                    }
                }

                callback?.Invoke(found);
            }

            EditorApplication.update += Update;
        }

        public static bool IsPackageInstalledSync(string packageName)
        {
            var request = Client.List(true, true);  // inclui também pacotes embutidos/indiretos
            while (!request.IsCompleted)
            {
                // poderia bloquear ou aguardar — cuidado com travamento no editor
            }

            if (request.Status == StatusCode.Success)
            {
                return request.Result.Any(p => p.name == packageName);
            }
            else
            {
                Debug.LogError("Erro ao listar pacotes: " + request.Error.message);
                return false;
            }
        }

        public static async Task<bool> IsPackageInstalledAsync(string packageName,bool forceInstallation)
        {
            bool isInstalled = await IsPackageInstalledAsync(packageName);
            if (!isInstalled && forceInstallation)
            {
                bool installResult = await InstallPackageAsync(packageName);
                return installResult;
            }
            return isInstalled;
        }
        public static async Task<bool> IsPackageInstalledAsync(string packageName)
        {
            ListRequest request = Client.List(); // lista todos os pacotes (inclusive integrados)

            while (!request.IsCompleted)
                await Task.Delay(50);

            if (request.Status == StatusCode.Failure)
            {
                UnityEngine.Debug.LogError($"Erro ao listar pacotes: {request.Error.message}");
                return false;
            }

            foreach (var p in request.Result)
            {
                if (p.name == packageName)
                    return true;
            }

            return false;
        }

        public static void InstallPackage(string packageName)
        {
            Debug.Log($"Tentando instalar pacote: {packageName}");
            AddRequest request = Client.Add(packageName);

            // Polling via EditorApplication.update
            EditorApplication.update += () =>
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= null; // remove callback antigo

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"Pacote instalado: {request.Result.name} ({request.Result.version})");
                }
                else
                {
                    Debug.LogError($"Erro ao instalar pacote {packageName}: {request.Error.message}");
                }
            };
        }

        public static async Task<bool> InstallPackageAsync(string packageName)
        {
            // Só instala se não estiver presente
            bool exists = await SmartTools.IsPackageInstalledAsync(packageName);
            if (exists)
            {
                Debug.Log($"Pacote já instalado: {packageName}");
                return true;
            }

            Debug.Log($"Instalando pacote: {packageName}...");
            AddRequest addRequest = Client.Add(packageName);

            while (!addRequest.IsCompleted)
                await Task.Delay(50);

            if (addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"Pacote instalado com sucesso: {addRequest.Result.name} ({addRequest.Result.version})");
                return true;
            }
            else
            {
                Debug.LogError($"Erro ao instalar pacote {packageName}: {addRequest.Error.message}");
                return false;
            }
        }

    }

}

#endif