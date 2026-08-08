using UnityEngine;
using UnityEngine.UIElements;

public static class GameBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Bootstrap()
    {
        Debug.Log("[GameBootstrapper] Starting programmatic generation of the game...");

        // 1. Setup Camera
        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        camGo.transform.position = new Vector3(0, 10, -5);
        camGo.transform.rotation = Quaternion.Euler(60, 0, 0);

        // 2. Setup Lighting
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

        // 3. Instantiate UI Toolkit
        var uiGo = new GameObject("UIDocument");
        var uiDoc = uiGo.AddComponent<UIDocument>();
        var visualTree = Resources.Load<VisualTreeAsset>("UI/AppUI");
        uiDoc.visualTreeAsset = visualTree;

        // 4. Instantiate Managers
        var appGo = new GameObject("AppController");
        
        var screenManager = appGo.AddComponent<ScreenManager>();
        screenManager.InitializeUI(uiDoc);

        appGo.AddComponent<AppController>();
        
        // PieceManager & BoardScene3D
        var boardGo = new GameObject("BoardScene3D");
        var boardScene = boardGo.AddComponent<BoardScene3D>();

        var pieceManagerGo = new GameObject("PieceManager");
        var pieceManager = pieceManagerGo.AddComponent<PieceManager>();
        
        // Reflection to inject boardScene into pieceManager since it's private
        var field = typeof(PieceManager).GetField("boardScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null) field.SetValue(pieceManager, boardScene);

        // Optional: spawn a literal 3D board mesh from Resources
        var boardMesh = Resources.Load<GameObject>("Models/board");
        if (boardMesh != null) {
            Object.Instantiate(boardMesh, new Vector3(0, 0, 0), Quaternion.identity);
        }

        Debug.Log("[GameBootstrapper] Bootstrapping complete!");
    }
}
