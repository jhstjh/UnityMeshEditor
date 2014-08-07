/*  Mesh Editor for Unity
 *  Version 1.0
 *  Created By Jihui Shentu
 *  2014 All Rights Reserved
 */

using UnityEngine;
using UnityEditor;
using System.Collections;
using ME;

public class MeshEditorMenu : MonoBehaviour {

    [MenuItem("Window/Mesh Editor Panel", false, 0)]
    public static void ShowWindow() {
        EditorWindow window = EditorWindow.GetWindow(typeof(MeshEditor));
        window.minSize = new Vector2(350, 450);
    }
}