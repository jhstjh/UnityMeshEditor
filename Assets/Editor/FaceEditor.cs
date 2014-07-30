using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System;

//[ExecuteInEditMode]
public class FaceEditor : EditorWindow {

    bool skinedMesh = true;
    bool createNewMaterial = true;
    bool movingFace = false;
    bool rotFace = false;
    bool scaleFace = false;
    string colliderSizeString;
    GameObject selObj = null;
    Mesh mesh = null;
    bool hasMeshCollider = false;
    bool succeed = false;
    bool prepared = false;
    bool rmbHold = false;
    Vector2 rmbMousePos;

    int moveCoord = 0;
    Vector3 lastHandlePos;
    Vector3 handlePos;

    int rotCoord = 0;
    Quaternion lastHandleRot;
    Quaternion handleRot;

    int scaleCoord = 0;
    Vector3 lastHandleScale;
    Vector3 handleScale;

    List<int> triangleIndices;
    List<List<List<int>>> realTriangleArrayWithSubMeshSeparated = new List<List<List<int>>>();
    List<List<int>> selectedFaces = new List<List<int>>();
    Material assignedMat = null;

    EditMode editMode = EditMode.Object;

    enum EditMode {
        Object  = 0,
        Vertex  = 1,
        Edge    = 2,
        Face    = 3
    }


    [MenuItem("Mesh Editor/Face Editor")]
    public static void ShowWindow() {
        EditorWindow.GetWindow(typeof(FaceEditor));
        //FaceEditMode();
    }

    void OnGUI() {
        GUILayout.BeginVertical();
        {
            GUILayout.Label("Face Editor");
            GUILayout.BeginHorizontal();

            editMode = (EditMode)EditorGUILayout.EnumPopup("Edit Mode", editMode);
            if (GUILayout.Button("Edit")) {
                if (editMode == EditMode.Face)
                    FaceEditMode();
            }
            //skinedMesh = GUILayout.Toggle(skinedMesh, "Create New Mesh");
            GUILayout.EndHorizontal();
            //GUILayout.Label("(Please don't touch anything in Scene \nbefore finishing the whole process)", GUILayout.Width(300));
            GUILayout.Space(10);
            
            GUILayout.BeginVertical();
            //GUI.enabled = succeed;
            GUILayout.Label("Change material for selected faces");
            if (GUILayout.Button("Change Material")) {
                ChangeMaterial();
            }
            createNewMaterial = GUILayout.Toggle(createNewMaterial, "Create New Material");
            if (createNewMaterial) {

            }
            else {
                assignedMat = EditorGUILayout.ObjectField("Material to use", assignedMat, typeof(Material), true) as Material;
            }

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

    void HandleHotKey(Event e) {
        if (e.keyCode == KeyCode.Q) {
            movingFace = false;
            rotFace = false;
            scaleFace = false;
            e.Use();
        }
        else if (e.keyCode == KeyCode.W) {
            movingFace = true;
            rotFace = false;
            scaleFace = false;
            e.Use();
        }
        else if (e.keyCode == KeyCode.E) {
            movingFace = false;
            rotFace = true;
            scaleFace = false;
            e.Use();
        }
        else if (e.keyCode == KeyCode.R) {
            movingFace = false;
            rotFace = false;
            scaleFace = true;
            e.Use();
        }
    }


    void OnSceneGUI(SceneView scnView) {
        Event evt = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        DrawToolBar();

        if (Selection.activeGameObject == null) {
            // reset tool here
            selObj = null;
            mesh = null;
            selectedFaces.Clear();
        }

        //bool hasHeld = false;

        if (evt.isMouse) {
            if (evt.button == 1) {
                if (rmbHold == false) {
                    rmbMousePos = evt.mousePosition;
                    rmbHold = true;
                    evt.Use();
                }
            }
            else {
                if (rmbHold == true) {
                    rmbHold = false;
                    
                    Rect vertexRect = new Rect(rmbMousePos.x - 100, rmbMousePos.y, 50, 20);
                    Rect edgeRect = new Rect(rmbMousePos.x, rmbMousePos.y - 50, 50, 20);
                    Rect faceRect = new Rect(rmbMousePos.x + 100, rmbMousePos.y, 50, 20);
                    Rect objRect = new Rect(rmbMousePos.x, rmbMousePos.y + 50, 50, 20);

                    
                    if (vertexRect.Contains(evt.mousePosition))
                        editMode = EditMode.Vertex;
                    else if (edgeRect.Contains(evt.mousePosition))
                        editMode = EditMode.Edge;
                    else if (faceRect.Contains(evt.mousePosition))
                        editMode = EditMode.Face;
                    else if (objRect.Contains(evt.mousePosition))
                        editMode = EditMode.Object;
                    HandleUtility.Repaint();
                }
            }
        }

        //else if (evt.type == EventType.Layout) {
            if (rmbHold && selObj != null) {
                //Debug.Log(rmbMousePos);
                GL.PushMatrix();
                GL.LoadOrtho();
                GL.Begin(GL.LINES);
                GL.Color(Color.white);
                GL.Vertex(new Vector3(rmbMousePos.x / Screen.width, 1 - rmbMousePos.y / Screen.height, 0));
                GL.Vertex(new Vector3(evt.mousePosition.x / Screen.width, 1 - evt.mousePosition.y / Screen.height, 0));
                GL.End();
                GL.PopMatrix();

                HandleUtility.Repaint();

                Handles.BeginGUI();
                GUILayout.Window(6, new Rect(rmbMousePos.x - 100, rmbMousePos.y, 50, 20), (subid) => { GUILayout.Label("Vertex"); }, " ");
                GUILayout.Window(7, new Rect(rmbMousePos.x, rmbMousePos.y - 50, 50, 20), (subid) => { GUILayout.Label("Edge"); }, " ");
                GUILayout.Window(8, new Rect(rmbMousePos.x + 100, rmbMousePos.y, 50, 20), (subid) => { GUILayout.Label("Face"); }, " ");
                GUILayout.Window(9, new Rect(rmbMousePos.x, rmbMousePos.y + 50, 50, 20), (subid) => { GUILayout.Label("Object"); }, " ");
                Handles.EndGUI();
            }
        //}


        if (editMode == EditMode.Face && mesh != null) {
            if (evt.isKey) {
                HandleHotKey(evt);
            }
            HighlightSelectedFaces();
            HandleFaceSelection(Event.current);

            if (movingFace && selectedFaces.Count != 0) 
                MoveFace();
            else if (rotFace && selectedFaces.Count != 0) 
                RotateFace();   
            else if (scaleFace && selectedFaces.Count != 0) 
                ScaleFace();

            HandleUtility.Repaint();
        }
    }

    void HandleFaceSelection(Event evt) {
        Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        RaycastHit hitInfo;
        if (Physics.Raycast(worldRay, out hitInfo) && hitInfo.collider.gameObject == selObj) {
            //Debug.Log(hitInfo.triangleIndex);

            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1, 0, 0, 0.5f));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 1]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]]));
            GL.End();

            if (evt.type == EventType.MouseDown) {
                List<int> selectedFace = new List<int>();
                selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex]);
                selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex + 1]);
                selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex + 2]);

                bool removed = false;
                for (int i = 0; i < selectedFaces.Count; i++) {
                    // if it already exists, remove it from selection, not working? TODO
                    if (selectedFace.SequenceEqual(selectedFaces[i])) {
                        selectedFaces.RemoveAt(i);
                        removed = true;
                    }
                }
                if (!Event.current.control) {
                    selectedFaces.Clear();
                }

                if (!removed)
                    selectedFaces.Add(selectedFace);

                handlePos = GetFacesAveragePosition(selectedFaces);
                handleRot = selObj.transform.rotation;
                handleScale = Vector3.one;
            }
        }
    }

    void HighlightSelectedFaces() {
        foreach (List<int> selectedFace in selectedFaces) {
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1, 1, 0, 0.5f));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[0]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[1]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[2]]));
            GL.End();
        }
    }

    void MoveFace() {
        Quaternion rot = new Quaternion();

        if (moveCoord == 0)
            rot = selObj.transform.rotation;
        else if (moveCoord == 1)
            rot = Quaternion.identity;
        else if (moveCoord == 2)
            rot = Quaternion.LookRotation(GetFaceNormal(selectedFaces[0]));

        lastHandlePos = handlePos;
        handlePos = Handles.PositionHandle(handlePos, rot);

        HashSet<int> modifiedIndex = new HashSet<int>();
        if (lastHandlePos != handlePos) {
            Vector3[] vertices = mesh.vertices;
            foreach (List<int> face in selectedFaces) {
                foreach (int vertex in face) {
                    if (!modifiedIndex.Contains(vertex)) {
                        vertices[vertex] += selObj.transform.InverseTransformDirection(handlePos - lastHandlePos);
                        modifiedIndex.Add(vertex);
                    }
                }
            }
            mesh.vertices = vertices;
            UpdateMeshCollider();
        }  
    }

    void RotateFace() {
        // not just work out of box
        // need transformation to different coord and maintain offset
        /*
        if (rotCoord == 0)
            rot = selObj.transform.rotation;
        else if (rotCoord == 1)
            rot = Quaternion.identity;
        else if (rotCoord == 2)
            rot = Quaternion.LookRotation(GetFaceNormal(selectedFaces[0]));
        */

        lastHandleRot = handleRot;
        handleRot = Handles.RotationHandle(handleRot, handlePos);
        //Debug.Log(handleRot);

        HashSet<int> modifiedIndex = new HashSet<int>();
        if (lastHandleRot != handleRot) { // does not work!
            //Debug.Log("Rotate");
            Vector3[] vertices = mesh.vertices;
            foreach (List<int> face in selectedFaces) {
                foreach (int vertex in face) {
                    if (!modifiedIndex.Contains(vertex)) {
                        Vector3 centerToVert = selObj.transform.TransformPoint(vertices[vertex]) - handlePos;
                        Quaternion oldToNewRot = handleRot * Quaternion.Inverse(lastHandleRot);
                        Vector3 newCenterToVert = oldToNewRot * centerToVert;
                        vertices[vertex] = selObj.transform.InverseTransformPoint(handlePos + newCenterToVert);
                        modifiedIndex.Add(vertex);
                    }
                }
            }
            mesh.vertices = vertices;
            UpdateMeshCollider();
        }
    }

    void ScaleFace() {
        Quaternion rot = new Quaternion();

        if (scaleCoord == 0)
            rot = selObj.transform.rotation;
        else if (scaleCoord == 1)
            rot = Quaternion.identity;
        else if (scaleCoord == 2)
            rot = Quaternion.LookRotation(GetFaceNormal(selectedFaces[0]));

        lastHandleScale = handleScale;
        handleScale = Handles.ScaleHandle(handleScale, handlePos, rot, 2.5f);
        //Debug.Log(handleScale);

        if (lastHandleScale != handleScale) {
            HashSet<int> modifiedIndex = new HashSet<int>();

            Vector3[] vertices = mesh.vertices;
            foreach (List<int> face in selectedFaces) {
                foreach (int vertex in face) {
                    if (!modifiedIndex.Contains(vertex)) {
                        Vector3 centerToVert = vertices[vertex] - selObj.transform.InverseTransformPoint(handlePos);
                        centerToVert.x *= (handleScale.x / lastHandleScale.x);
                        centerToVert.y *= (handleScale.y / lastHandleScale.y);
                        centerToVert.z *= (handleScale.z / lastHandleScale.z);

                        vertices[vertex] = selObj.transform.InverseTransformPoint(handlePos) + centerToVert;
                        modifiedIndex.Add(vertex);
                    }
                }
            }
            mesh.vertices = vertices;
            UpdateMeshCollider();
        }
    }

    void DrawToolBar() {
        Handles.BeginGUI();

        GUILayout.Window(2, new Rect(10, 20, 50, 50), (id) => {
            movingFace = GUILayout.Toggle(movingFace, EditorGUIUtility.Load("move.png") as Texture, "Button");
            if (movingFace) {
                rotFace = false;
                scaleFace = false;
            }
            rotFace = GUILayout.Toggle(rotFace, EditorGUIUtility.Load("rotate.png") as Texture, "Button");
            if (rotFace) {
                movingFace = false;
                scaleFace = false;
            }
            scaleFace = GUILayout.Toggle(scaleFace, EditorGUIUtility.Load("scale.png") as Texture, "Button");
            if (scaleFace) {
                movingFace = false;
                rotFace = false;
            }
        }, "Tools");

        if (movingFace) {
            GUILayout.Window(3, new Rect(80, 40, 50, 50), (subid) => {
                GUILayout.Label("Move Axis:");
                string[] content = { "Local", "World", "UVN" };
                moveCoord = GUILayout.SelectionGrid(moveCoord, content, 3, "toggle");
            }, "Move Tool");
        }
        else if (rotFace) {
            GUILayout.Window(4, new Rect(80, 80, 50, 50), (subid) => {
                GUILayout.Label("Rotate Axis:");
                string[] content = { "Local", "World", "UVN" };
                rotCoord = GUILayout.SelectionGrid(rotCoord, content, 3, "toggle");
            }, "Rotate Tool");
        }
        else if (scaleFace) {
            GUILayout.Window(5, new Rect(80, 120, 50, 50), (subid) => {
                GUILayout.Label("Scale Axis:");
                string[] content = { "Local", "World", "UVN" };
                scaleCoord = GUILayout.SelectionGrid(scaleCoord, content, 3, "toggle");
            }, "Scale Tool");
        }


        Handles.EndGUI();
    }
    Vector3 GetFacesAveragePosition(List<List<int>> faces) {
        Vector3 pos = Vector3.zero;
        foreach (List<int> face in faces) {
            pos += GetFaceAveragePosition(face);
        }
        return pos / faces.Count;
    }

    Vector3 GetFaceAveragePosition(List<int> face) {
        return (selObj.transform.TransformPoint(mesh.vertices[face[0]]) + selObj.transform.TransformPoint(mesh.vertices[face[1]]) + selObj.transform.TransformPoint(mesh.vertices[face[2]])) / 3;
    }


    Vector3 GetFacesNormal(List<List<int>> faces) {
        Vector3 pos = Vector3.zero;
        foreach (List<int> face in faces) {
            pos += GetFaceNormal(face);
        }
        return pos / faces.Count;
    }

    Vector3 GetFaceNormal(List<int> face) {
        Vector3 edge1 = selObj.transform.TransformPoint(mesh.vertices[face[1]]) - selObj.transform.TransformPoint(mesh.vertices[face[0]]);
        Vector3 edge2 = selObj.transform.TransformPoint(mesh.vertices[face[2]]) - selObj.transform.TransformPoint(mesh.vertices[face[1]]);

        return Vector3.Cross(edge1, edge2).normalized;
    }

    void UpdateMeshCollider() {
        selObj.GetComponent<MeshCollider>().sharedMesh = null;
        selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
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

        if (!createNewMaterial && assignedMat != null) {
            materials.Add(assignedMat);
            
        }
        else {
            Material newMat = new Material(Shader.Find("Diffuse"));
            AssetDatabase.CreateAsset(newMat, "Assets/Material/newMat_" + newMesh.subMeshCount + ".mat");
            AssetDatabase.Refresh();
            newMat.color = Color.blue;

            materials.Add(newMat);
        }

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
        FaceEditMode();
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
        SceneView.onSceneGUIDelegate += this.OnSceneGUI;
    }

    void OnDestroy() {
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
    }
}