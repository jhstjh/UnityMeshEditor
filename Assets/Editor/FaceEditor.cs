using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

//[ExecuteInEditMode]
public class FaceEditor : EditorWindow {

    //static GameObject obj;
    bool skinedMesh = true;
    bool createNewMaterial = true;
    string colliderSizeString;
    GameObject selObj;
    Mesh mesh;
    bool hasMeshCollider = false;
    bool succeed = false;
    bool prepared = false;
    List<int> triangleIndices;
    List<List<List<int>>> realTriangleArrayWithSubMeshSeparated = new List<List<List<int>>>();
    //static List<List<int>> realTriangleArray = new List<List<int>>();
    List<List<int>> selectedFaces = new List<List<int>>();



    [MenuItem("Mesh Editor/Face Editor")]
    public static void ShowWindow() {
        EditorWindow.GetWindow(typeof(FaceEditor));
        //FaceEditMode();
    }

    void OnGUI() {
        GUILayout.BeginVertical();
        {
            GUILayout.Label("Separate Selected Mesh");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Face Editing Mode")) {
                FaceEditMode();
            }
            skinedMesh = GUILayout.Toggle(skinedMesh, "Create New Mesh");
            GUILayout.EndHorizontal();
            GUILayout.Label("(Please don't touch anything in Scene \nbefore finishing the whole process)", GUILayout.Width(300));
            GUILayout.Space(10);

            GUILayout.BeginVertical();
            //GUI.enabled = succeed;
            GUILayout.Label("Change material for selected faces");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Change Material")) {
                ChangeMaterial();
            }
            createNewMaterial = GUILayout.Toggle(createNewMaterial, "Create New Material");
            if (createNewMaterial) {

            }

            GUILayout.EndHorizontal();
            //GUI.enabled = prepared;
            GUILayout.Space(10);
            GUILayout.Label("Save and link prefab to current object");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save & Link")) {
                //GeneratePrefabAndLink();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
        GUILayout.EndVertical();

        //Debug.Log("FaceEdit");
    }


    void FaceEditMode() {
        selObj = Selection.activeGameObject;
        mesh = selObj.GetComponent<MeshFilter>().sharedMesh;

        if (selObj.GetComponent<MeshCollider>() == null) {
            MeshCollider mc = selObj.AddComponent<MeshCollider>();
            mc.sharedMesh = selObj.GetComponent<MeshFilter>().sharedMesh;
        }
        else
            hasMeshCollider = true;

        for (int i = 0; i < mesh.subMeshCount; i++) {
            List<List<int>> realTriangleArray = new List<List<int>>();
            List<int> subMeshTriangles = new List<int>(mesh.GetTriangles(i));

            for (int j = 0; j < subMeshTriangles.Count; j += 3) {
                List<int> face = new List<int>();
                face.Add(subMeshTriangles[j]);
                face.Add(subMeshTriangles[j + 1]);
                face.Add(subMeshTriangles[j + 2]);
                realTriangleArray.Add(face);
            }
            realTriangleArrayWithSubMeshSeparated.Add(realTriangleArray);
        }
    }

    void OnSceneGUI(SceneView scnView) {
        //Debug.Log(Event.current.mousePosition);
        if (mesh != null) {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            foreach (List<int> selectedFace in selectedFaces) {
                GL.Begin(GL.TRIANGLES);
                GL.Color(new Color(1, 1, 0, 0.5f));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[0]]));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[1]]));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[2]]));
                GL.End();
            }
            Ray worldRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            RaycastHit hitInfo;
            if (Physics.Raycast(worldRay, out hitInfo)) {
                if (hitInfo.collider.gameObject != selObj) return;

                Debug.Log(hitInfo.triangleIndex);
                GL.Begin(GL.TRIANGLES);
                GL.Color(new Color(1, 0, 0, 0.5f));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex]]));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 1]]));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]]));
                GL.End();
                if (Event.current.type == EventType.MouseDown) {
                    List<int> selectedFace = new List<int>();
                    selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex]);
                    selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex + 1]);
                    selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex + 2]);

                    for (int i = 0; i < selectedFaces.Count; i++) {
                        // if it already exists, remove it from selection
                        if (selectedFace.SequenceEqual(selectedFaces[i])) {
                            selectedFaces.RemoveAt(i);
                            return;
                        }
                    }
                    if (!Event.current.control) {
                        selectedFaces.Clear();
                    }
                    selectedFaces.Add(selectedFace);
                }
            }
        }
    }


    void ChangeMaterial() {
        
        Mesh newMesh = new Mesh();

        newMesh.vertices = mesh.vertices;
        newMesh.normals = mesh.normals;
        newMesh.uv = mesh.uv;
        newMesh.subMeshCount = mesh.subMeshCount;
 
        Debug.Log("SubMesh before Operation: " + newMesh.subMeshCount);
        
        int subMeshIdx = 0;
        foreach (List<List<int>> realTriangleArray in realTriangleArrayWithSubMeshSeparated)
        {
            for (int idx = 0; idx < realTriangleArray.Count; /*idx++*/) {
                bool removed = false;
                foreach (List<int> selectedFace in selectedFaces) {
                    if (selectedFace.SequenceEqual(realTriangleArray[idx])) {
                        realTriangleArray.RemoveAt(idx);
                        removed = true;
                        break;
                    }
                }
                if (!removed)
                    idx++;
            }

            List<int> newTriangleList = new List<int>();

            foreach (List<int> triangle in realTriangleArray) {
                newTriangleList.AddRange(triangle);
            }

            newMesh.SetTriangles(newTriangleList.ToArray(), subMeshIdx);
            subMeshIdx++;
        }
        newMesh.subMeshCount += 1;

        List<int> selectedTriangleList = new List<int>();
        foreach (List<int> selectedFace in selectedFaces) {
            selectedTriangleList.AddRange(selectedFace);
        }
        newMesh.SetTriangles(selectedTriangleList.ToArray(), newMesh.subMeshCount - 1);

        AssetDatabase.CreateAsset(newMesh, "Assets/Mesh/" + selObj.name);
        AssetDatabase.Refresh();
        
        List<Material> materials = new List<Material>();
        materials.AddRange(selObj.renderer.sharedMaterials);

        Material newMat = new Material(Shader.Find("Diffuse"));
        AssetDatabase.CreateAsset(newMat, "Assets/Material/newMat_" + newMesh.subMeshCount + ".mat");
        AssetDatabase.Refresh();
        newMat.color = Color.blue;

        materials.Add(newMat);

        newMesh.Optimize();

        selObj.GetComponent<MeshFilter>().sharedMesh = newMesh;
        selObj.renderer.sharedMaterials = materials.ToArray();

        Debug.Log("SubMesh after Operation: " + newMesh.subMeshCount);

        if (hasMeshCollider)
            selObj.GetComponent<MeshCollider>().sharedMesh = newMesh;
        else
            Destroy(selObj.GetComponent<MeshCollider>());
        realTriangleArrayWithSubMeshSeparated.Clear();
        selectedFaces.Clear();
        Selection.objects = new UnityEngine.Object[0];
        selObj = null;
        mesh = null;
    }

    void OnFocus() {
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
        SceneView.onSceneGUIDelegate += this.OnSceneGUI;
    }

    void OnDestroy() {
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
    }
}