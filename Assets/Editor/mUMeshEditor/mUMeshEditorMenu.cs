/*  Mesh Editor for Unity
 *  Version 1.11
 *  Created By Jihui Shentu
 *  2014 All Rights Reserved
 */

using UnityEngine;
using UnityEditor;
using System.Collections;
using MU;

public class mUMeshEditorMenu : MonoBehaviour {

    [MenuItem("Window/mU Mesh Editor Panel", false, 0)]
    public static void ShowWindow() {
        EditorWindow window = EditorWindow.GetWindow(typeof(MeshEditor));
        window.minSize = new Vector2(350, 450);
    }
}