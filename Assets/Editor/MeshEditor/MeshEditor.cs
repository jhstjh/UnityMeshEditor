/*  Mesh Editor for Unity
 *  Version 1.0
 *  Created By Jihui Shentu
 *  2014 All Rights Reserved
 */

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ME {
    public class MeshEditor : EditorWindow {
        bool createNewMaterial = true;
        bool moveElement = false;
        bool rotElement = false;
        bool scaleElement = false;
        bool hasMeshCollider = false;
        bool rmbHold = false;
        bool lmbHold = false;
        bool keepFaceTogether = true;
        bool editOnOriginalMesh = false;
        bool holdingHandle = false;
        bool useGLDraw = false;
        string materialPath = "Assets";
        string meshPath = "Assets/newMesh";
        Vector2 rmbMousePos;
        Vector2 lmbDownPos;

        GameObject selObj = null;
        Mesh mesh = null;

        int moveCoord = 0;
        Vector3 lastHandlePos;
        Vector3 handlePos;

        int rotCoord = 0;
        Quaternion lastHandleRot;
        Quaternion handleRot;

        int scaleCoord = 0;
        Vector3 lastHandleScale;
        Vector3 handleScale;

        List<List<List<int>>> realTriangleArrayWithSubMeshSeparated = new List<List<List<int>>>();
        List<List<int>> selectedFaces = new List<List<int>>();
        List<int> selectedFacesIndex = new List<int>();
        List<List<int>> selectedEdges = new List<List<int>>();
        List<int> selectedVertices = new List<int>();
        List<BoxCollider> bColliders = new List<BoxCollider>();
        List<SphereCollider> sColliders = new List<SphereCollider>();
        List<CapsuleCollider> cColliders = new List<CapsuleCollider>();
        Dictionary<int, int> vertexMapping = new Dictionary<int, int>();
        Dictionary<HashSet<int>, int> edgeOccurance = new Dictionary<HashSet<int>, int>(new HashSetEqualityComparer<int>());
        Dictionary<Mesh, int> meshReferenceCount = new Dictionary<Mesh, int>();
        Material assignedMat = null;
        List<KeyValuePair<Mesh, EditType>> meshUndoList = new List<KeyValuePair<Mesh, EditType>>(10);

        EditMode editMode = EditMode.Object;

        enum EditMode {
            Object = 0,
            Vertex = 1,
            Edge = 2,
            Face = 3
        }

        enum EditType {
            Move,
            Rotate,
            Scale,
            Extrude,
            Harden,
            ChangeMat,
            DelFace
        }

        class HashSetEqualityComparer<T> : IEqualityComparer<HashSet<T>> {
            public int GetHashCode(HashSet<T> hashSet) {
                if (hashSet == null)
                    return 0;
                int h = 0x14345843;//some arbitrary number
                foreach (T elem in hashSet) {
                    h = h + hashSet.Comparer.GetHashCode(elem);
                }
                return h;
            }

            public bool Equals(HashSet<T> set1, HashSet<T> set2) {
                if (set1 == set2)
                    return true;
                if (set1 == null || set2 == null)
                    return false;
                return set1.SetEquals(set2);
            }
        }

        void OnGUI() {
            GUILayout.BeginVertical();
            {
                EditorGUILayout.LabelField("Mesh Editor 1.0", EditorStyles.boldLabel);
                //useGLDraw = GUILayout.Toggle(useGLDraw, "Use GL for highlighting");
                //GUILayout.Space(10);
                EditMode newMode = (EditMode)EditorGUILayout.EnumPopup("Edit Mode", editMode);
                if (newMode != editMode) {
                    if (editMode == EditMode.Object)
                        MeshEditMode();
                    if (newMode == EditMode.Object)
                        ExitMeshEditMode();
                    editMode = newMode;
                }
                editOnOriginalMesh = GUILayout.Toggle(editOnOriginalMesh, "Edit On All Instances");
                GUILayout.Label("Notice: Mesh Editor will NOT modify the source file (e.g. \n" +
                                "*.fbx) but just the imported mesh.\n" +
                                "Select \"Edit On All Instances\" will affect all instances\n" +
                                "in scene, otherwise a new mesh copy will be created. You \n" +
                                "have to save it before you can use it for a prefab.", GUILayout.Width(400));
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Save Modified Mesh As: ");
                meshPath = EditorGUILayout.TextField(meshPath);
                if (GUILayout.Button("Browse")) {
                    string returnPath = EditorUtility.SaveFilePanelInProject("Save Mesh To...", mesh.name, "", "");
                    if (returnPath.Length != 0) {
                        int startPathIdx = returnPath.IndexOf("Assets");
                        if (startPathIdx != -1)
                            meshPath = returnPath.Substring(startPathIdx);
                        else
                            Debug.LogError("This is not a valid path!");
                    }
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Save Mesh", GUILayout.ExpandWidth(false))) {
                    if (mesh != null) {
                        AssetDatabase.CreateAsset(mesh, meshPath);
                        AssetDatabase.Refresh();
                    }
                }
                GUILayout.Space(20);

                EditorGUILayout.LabelField("Face Editing Tools", EditorStyles.boldLabel);
                GUI.enabled = selectedFaces.Count > 0;

                GUILayout.Label("Extrude Selected Faces");
                keepFaceTogether = GUILayout.Toggle(keepFaceTogether, "Keep face together");
                if (GUILayout.Button("Extrude", GUILayout.ExpandWidth(false))) {
                    Extrude();
                }
                GUILayout.Space(10);

                GUILayout.Label("Harden selected face edge. \nThis will extract the faces from adjacent faces.\nHelpful for fixing weird normals after extrusion.");
                if (GUILayout.Button("Harden face edge", GUILayout.ExpandWidth(false))) {
                    if (mesh) {
                        HardenFaceEdge();
                    }
                }
                GUILayout.Space(10);

                GUILayout.Label("Change material for selected faces");
                createNewMaterial = GUILayout.Toggle(createNewMaterial, "Create New Material");
                if (createNewMaterial) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Save New Material To: ");
                    materialPath = EditorGUILayout.TextField(materialPath);
                    if (GUILayout.Button("Browse")) {
                        string returnPath = EditorUtility.SaveFolderPanel("Save Material To...", materialPath, "");
                        if (returnPath.Length != 0) {
                            int startPathIdx = returnPath.IndexOf("Assets");
                            if (startPathIdx != -1)
                                materialPath = returnPath.Substring(startPathIdx);
                            else
                                Debug.LogError("This is not a valid path!");
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                else {
                    assignedMat = EditorGUILayout.ObjectField("Material to use", assignedMat, typeof(Material), true) as Material;
                }
                if (GUILayout.Button("Change Material", GUILayout.ExpandWidth(false))) {
                    ChangeMaterial();
                }
                GUILayout.Space(10);
            }
            GUILayout.EndVertical();
            GUI.enabled = true;
        }

        void MeshEditMode() {
            ExitMeshEditMode();
            CheckMeshReferenceCount();
            // in case scale tool is selected and breaks GL function
            Tools.current = Tool.None;
			//Tools.hidden = true;
            selObj = Selection.activeGameObject;
            if (selObj == null) {
                Debug.LogError("No Object Selected!");
                return;
            }

            if (selObj.GetComponent<MeshFilter>() == null) {
                Debug.LogError("Selected object has no mesh filter attached! Mesh Editor cannot get the mesh copy!");
                return;
            }

            if (editOnOriginalMesh)
                mesh = selObj.GetComponent<MeshFilter>().sharedMesh;
            else {
                mesh = (Mesh)Instantiate(selObj.GetComponent<MeshFilter>().sharedMesh);
                mesh.name = selObj.GetComponent<MeshFilter>().sharedMesh.name;
               
                if (meshReferenceCount[selObj.GetComponent<MeshFilter>().sharedMesh] == 1) {
                    meshReferenceCount.Remove(selObj.GetComponent<MeshFilter>().sharedMesh);
                    if (!AssetDatabase.Contains(selObj.GetComponent<MeshFilter>().sharedMesh)) {
                        DestroyImmediate(selObj.GetComponent<MeshFilter>().sharedMesh);
                    }
                }
                else
                    meshReferenceCount[selObj.GetComponent<MeshFilter>().sharedMesh]--;
            
                selObj.GetComponent<MeshFilter>().sharedMesh = mesh;
            }

            if (selObj.GetComponent<MeshCollider>() == null) {
                MeshCollider mc = selObj.AddComponent<MeshCollider>();
                if (mc == null) {
                    Debug.LogError("Please select gameObject in scene. Mesh Editor does not support editing directly on prefabs.");
                    if (!editOnOriginalMesh)
                        DestroyImmediate(mesh);
                    ClearUndoList();
                    return;
                }
                mc.sharedMesh = selObj.GetComponent<MeshFilter>().sharedMesh;
                hasMeshCollider = false;
            }
            else {
                hasMeshCollider = true;
                if (!editOnOriginalMesh)
                    selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
            }

            BoxCollider[] boxColliders = selObj.GetComponentsInChildren<BoxCollider>();
            foreach (BoxCollider boxCollider in boxColliders) {
                if (boxCollider.enabled == true) {
                    bColliders.Add(boxCollider);
                    boxCollider.enabled = false;
                }
            }

            SphereCollider[] sphereColliders = selObj.GetComponentsInChildren<SphereCollider>();
            foreach (SphereCollider sphereCollider in sphereColliders) {
                if (sphereCollider.enabled == true) {
                    sColliders.Add(sphereCollider);
                    sphereCollider.enabled = false;
                }
            }

            CapsuleCollider[] capsuleColliders = selObj.GetComponentsInChildren<CapsuleCollider>();
            foreach (CapsuleCollider capsuleCollider in capsuleColliders) {
                if (capsuleCollider.enabled == true) {
                    cColliders.Add(capsuleCollider);
                    capsuleCollider.enabled = false;
                }
            }
        }

        void ExitMeshEditMode() {
            ClearUndoList();
            if (!selObj) return;

            if (hasMeshCollider) {
                selObj.GetComponent<MeshCollider>().sharedMesh = null;
                selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
            }
            else {
                if (selObj.GetComponent<MeshCollider>() != null)
                    DestroyImmediate(selObj.GetComponent<MeshCollider>());
            }

            foreach (BoxCollider boxCollider in bColliders) {
                if (boxCollider != null)
                    boxCollider.enabled = true;
            }

            foreach (SphereCollider sphereCollider in sColliders) {
                if (sphereCollider != null)
                    sphereCollider.enabled = true;
            }

            foreach (CapsuleCollider capsuleCollider in cColliders) {
                if (capsuleCollider != null)
                    capsuleCollider.enabled = true;
            }

            bColliders.Clear();
            cColliders.Clear();
            sColliders.Clear();

            selObj = null;
            mesh = null;
            selectedFaces.Clear();
            selectedVertices.Clear();
            selectedEdges.Clear();
            realTriangleArrayWithSubMeshSeparated.Clear();
            vertexMapping.Clear();
            selectedFacesIndex.Clear();
            meshReferenceCount.Clear();
            editMode = EditMode.Object;
        }

        void OnSceneGUI(SceneView scnView) {
            Event evt = Event.current;
            DrawToolBar();

            if (Event.current.type == EventType.ValidateCommand) {
                //Debug.Log(Event.current.commandName);
                if (Event.current.commandName == "UndoRedoPerformed") {
                    Event.current.Use();
                    UndoMeshChanges();
                }
            }

            if (Selection.activeGameObject == null && editMode != EditMode.Object) {
                ExitMeshEditMode();
                editMode = EditMode.Object;
            }
            else if (Selection.activeGameObject != null && Selection.activeGameObject != selObj) {
                ExitMeshEditMode();
                MeshEditMode();
            }

            if (evt.isKey && editMode != EditMode.Object) {
                if (evt.keyCode == KeyCode.Delete) {
                    DeleteFace();
                    evt.Use();
                }
            }

            DrawRectSelection(evt);
            HandleFastSel(evt);
            if (rmbHold) {
                DrawFastSel(evt);
            }

            if (editMode == EditMode.Face && mesh != null) {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (evt.isKey)
                    HandleHotKey(evt);
                if (evt.type == EventType.repaint)
                    HighlightSelectedFaces();
                if (moveElement && selectedFaces.Count != 0)
                    MoveVertexGroup(selectedFaces);
                else if (rotElement && selectedFaces.Count != 0)
                    RotateVertexGroup(selectedFaces);
                else if (scaleElement && selectedFaces.Count != 0)
                    ScaleVertexGroup(selectedFaces);
                HandleFaceSelection(Event.current);
                HandleUtility.Repaint();
            }
            else if (editMode == EditMode.Vertex && mesh != null) {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (evt.isKey)
                    HandleHotKey(evt);
                if (evt.type == EventType.repaint)
                    HighlightVertices();
                if (moveElement && selectedVertices.Count != 0)
                    MoveVertexGroup(new List<List<int>> { selectedVertices });
                if (rotElement && selectedVertices.Count != 0)
                    RotateVertexGroup(new List<List<int>> { selectedVertices });
                if (scaleElement && selectedVertices.Count != 0)
                    ScaleVertexGroup(new List<List<int>> { selectedVertices });
                HandleVertexSelection(Event.current);
                HandleUtility.Repaint();
            }
            else if (editMode == EditMode.Edge && mesh != null) {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (evt.isKey)
                    HandleHotKey(evt);
                if (evt.type == EventType.repaint)
                    HighlightSelectedEdges();
                if (moveElement && selectedEdges.Count != 0)
                    MoveVertexGroup(selectedEdges);
                else if (rotElement && selectedEdges.Count != 0)
                    RotateVertexGroup(selectedEdges);
                else if (scaleElement && selectedEdges.Count != 0)
                    ScaleVertexGroup(selectedEdges);
                HandleEdgeSelection(Event.current);
                HandleUtility.Repaint();
            }
            if (evt.type == EventType.MouseDown && !evt.alt && editMode != EditMode.Object) {
                if (evt.button == 0 && !lmbHold) {
                    lmbDownPos = evt.mousePosition;
                    lmbHold = true;
                }
            }
            else if (evt.type == EventType.MouseUp) {
                HandleRectSelection(evt);
            }
            Repaint();
        }

        bool AddNewFace(int vert1, int vert2, ref List<int> triangleList, List<int> selectedFace, int startNewIndex) {
            if (keepFaceTogether) {
                HashSet<int> theEdge = new HashSet<int>();
                theEdge.Add(Mathf.Min(selectedFace[vert1], selectedFace[vert2]));
                theEdge.Add(Mathf.Max(selectedFace[vert1], selectedFace[vert2]));

                if (edgeOccurance[theEdge] > 1)
                    return false;

                triangleList.Add(selectedFace[vert1]);
                triangleList.Add(selectedFace[vert2]);
                triangleList.Add(vertexMapping[selectedFace[vert1]]);
                triangleList.Add(vertexMapping[selectedFace[vert2]]);
                triangleList.Add(vertexMapping[selectedFace[vert1]]);
                triangleList.Add(selectedFace[vert2]);
                return true;
            }
            else {
                triangleList.Add(selectedFace[vert1]);
                triangleList.Add(selectedFace[vert2]);
                triangleList.Add(startNewIndex + vert1);
                triangleList.Add(startNewIndex + vert2);
                triangleList.Add(startNewIndex + vert1);
                triangleList.Add(selectedFace[vert2]);
                return true;
            }
        }

        void Extrude() {
            List<Vector3> vertexList = new List<Vector3>(mesh.vertices);
            List<Vector2> uvList = new List<Vector2>(mesh.uv);
            List<Vector3> normalList = new List<Vector3>(mesh.normals);
            List<int> triangleList = new List<int>(mesh.triangles);

            List<List<int>> extrudedFaces = new List<List<int>>();
            List<int> extrudedFacesIndex = new List<int>();

            CacheUndoMeshBackup(EditType.Extrude);
            edgeOccurance.Clear();
            foreach (List<int> selectedFace in selectedFaces) {
                for (int i = 0; i < 3; i++) {
                    HashSet<int> edge = new HashSet<int>();
                    edge.Add(Mathf.Min(selectedFace[i], selectedFace[(i + 1) % 3]));
                    edge.Add(Mathf.Max(selectedFace[i], selectedFace[(i + 1) % 3]));

                    if (edgeOccurance.ContainsKey(edge)) {
                        edgeOccurance[edge]++;
                    }
                    else {
                        edgeOccurance.Add(edge, 1);
                    }
                }
            }

            vertexMapping.Clear();
            int faceIdx = 0;
            foreach (List<int> selectedFace in selectedFaces) {
                int startNewIndex = vertexList.Count;
                foreach (int vertIdx in selectedFace) {
                    if (!vertexMapping.ContainsKey(vertIdx) || !keepFaceTogether) {
                        vertexList.Add(vertexList[vertIdx]);
                        normalList.Add(GetFaceNormal(selectedFace));
                        //normalList.Add(normalList[vertIdx]);
                        uvList.Add(uvList[vertIdx]);
                        triangleList.Add(vertexList.Count - 1);
                        if (keepFaceTogether)
                            vertexMapping.Add(vertIdx, vertexList.Count - 1);
                    }
                    else {
                        triangleList.Add(vertexMapping[vertIdx]);
                    }
                }

                extrudedFacesIndex.Add((triangleList.Count) / 3 - 1);

                AddNewFace(0, 1, ref triangleList, selectedFace, startNewIndex);
                AddNewFace(1, 2, ref triangleList, selectedFace, startNewIndex);
                AddNewFace(2, 0, ref triangleList, selectedFace, startNewIndex);

                List<int> extrudedFace = new List<int>();

                if (!keepFaceTogether) {
                    extrudedFace.Add(startNewIndex);
                    extrudedFace.Add(startNewIndex + 1);
                    extrudedFace.Add(startNewIndex + 2);
                }
                else {
                    extrudedFace.Add(vertexMapping[selectedFace[0]]);
                    extrudedFace.Add(vertexMapping[selectedFace[1]]);
                    extrudedFace.Add(vertexMapping[selectedFace[2]]);
                }
                extrudedFaces.Add(extrudedFace);

                triangleList.RemoveRange(3 * selectedFacesIndex[faceIdx], 3);
                for (int i = faceIdx + 1; i < selectedFacesIndex.Count; i++) {
                    if (selectedFacesIndex[i] > selectedFacesIndex[faceIdx]) {
                        selectedFacesIndex[i]--;
                    }
                }

                for (int i = 0; i < extrudedFacesIndex.Count; i++) {
                    if (extrudedFacesIndex[i] > selectedFacesIndex[faceIdx]) {
                        extrudedFacesIndex[i]--;
                    }
                }
                faceIdx++;
            }

            mesh.vertices = vertexList.ToArray();
            mesh.uv = uvList.ToArray();
            mesh.triangles = triangleList.ToArray();
            mesh.normals = normalList.ToArray();

            //mesh.Optimize();
            UpdateMeshCollider();

            selectedFaces = extrudedFaces;
            selectedFacesIndex = extrudedFacesIndex;

            moveElement = true;
            rotElement = false;
            scaleElement = false;

            moveCoord = 3;
        }

        void DeleteFace() {
            if (selectedFaces.Count == 0) return;

            CacheUndoMeshBackup(EditType.DelFace);
            List<int> triangleList = new List<int>(mesh.triangles);
            for (int i = 0; i < selectedFacesIndex.Count; i++) {           
                triangleList.RemoveRange(3 * selectedFacesIndex[i], 3);
                for (int j = i; j < selectedFacesIndex.Count; j++) {
                    if (selectedFacesIndex[i] < selectedFacesIndex[j])
                        selectedFacesIndex[j]--;
                }
            }
            selectedFaces.Clear();
            selectedFacesIndex.Clear();
            mesh.triangles = triangleList.ToArray();
            mesh.Optimize();
            UpdateMeshCollider();
        }

        void HardenFaceEdge() {
            CacheUndoMeshBackup(EditType.Harden);
            List<Vector3> vertexList = new List<Vector3>(mesh.vertices);
            List<Vector2> uvList = new List<Vector2>(mesh.uv);
            List<Vector3> normalList = new List<Vector3>(mesh.normals);
            List<int> triangleList = new List<int>(mesh.triangles);

            foreach (List<int> selectedFace in selectedFaces) {
                foreach (int vertex in selectedFace) {
                    vertexList.Add(vertexList[vertex]);
                    uvList.Add(uvList[vertex]);
                    //normalList.Add(uvList[vertex]);
                    normalList.Add(GetFaceNormal(selectedFace));
                    triangleList.Add(vertexList.Count - 1);

                }
            }

            while (selectedFacesIndex.Count != 0) {
                for (int j = 1; j < selectedFacesIndex.Count; j++) {
                    if (selectedFacesIndex[j] > selectedFacesIndex[0])
                        selectedFacesIndex[j]--;
                }
                triangleList.RemoveRange(3 * selectedFacesIndex[0], 3);
                selectedFacesIndex.RemoveAt(0);
            }

            mesh.vertices = vertexList.ToArray();
            mesh.uv = uvList.ToArray();
            mesh.normals = normalList.ToArray();
            mesh.triangles = triangleList.ToArray();

            ExitMeshEditMode();
        }

        void HandleFaceSelection(Event evt) {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            RaycastHit hitInfo;
            if (Physics.Raycast(worldRay, out hitInfo) && hitInfo.collider.gameObject == selObj) {
                if (evt.type == EventType.repaint) {
                    if (useGLDraw) {
                        GL.PushMatrix();
                        GL.Begin(GL.TRIANGLES);
                        GL.Color(new Color(1, 0, 0, 0.5f));
                        GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex]]));
                        GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 1]]));
                        GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]]));
                        GL.End();
                        GL.PopMatrix();
                    }
                    else {
                        Handles.DrawSolidRectangleWithOutline(new Vector3[] {selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex]]),
                                                                             selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 1]]),
                                                                             selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]]),
                                                                             selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]])},
                                                                             new Color(1, 0, 0, 0.5f), new Color(0, 0, 0, 1));
                    }
                }


                if (evt.type == EventType.MouseDown) {
                    List<int> selectedFace = new List<int>();
                    selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex]);
                    selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex + 1]);
                    selectedFace.Add(mesh.triangles[3 * hitInfo.triangleIndex + 2]);

                    bool removed = false;
                    for (int i = 0; i < selectedFaces.Count; i++) {
                        if (selectedFace.SequenceEqual(selectedFaces[i])) {
                            selectedFaces.RemoveAt(i);
                            selectedFacesIndex.Remove(hitInfo.triangleIndex);
                            removed = true;
                            break;
                        }
                    }
                    if (!Event.current.shift) {
                        selectedFaces.Clear();
                        selectedFacesIndex.Clear();
                    }

                    if (!removed) {
                        selectedFaces.Add(selectedFace);
                        selectedFacesIndex.Add(hitInfo.triangleIndex);
                    }

                    handlePos = GetFacesAveragePosition(selectedFaces);
                    handleRot = selObj.transform.rotation;
                    handleScale = Vector3.one;
                }
            }
        }

        void HandleEdgeSelection(Event evt) {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            RaycastHit hitInfo;
            if (Physics.Raycast(worldRay, out hitInfo) && hitInfo.collider.gameObject == selObj) {
                Vector3 vert1 = selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex]]);
                Vector3 vert2 = selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 1]]);
                Vector3 vert3 = selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]]);

                float dist1 = HandleUtility.DistanceToLine(vert1, vert2);
                float dist2 = HandleUtility.DistanceToLine(vert1, vert3);
                float dist3 = HandleUtility.DistanceToLine(vert2, vert3);

                float min = Mathf.Min(new float[] { dist1, dist2, dist3 });

                List<int> highLightedEdge = new List<int>();

                if (min < 10.0f) {
                    if (min == dist1) {
                        highLightedEdge.Add(mesh.triangles[3 * hitInfo.triangleIndex]);
                        highLightedEdge.Add(mesh.triangles[3 * hitInfo.triangleIndex + 1]);
                    }
                    else if (min == dist2) {
                        highLightedEdge.Add(mesh.triangles[3 * hitInfo.triangleIndex]);
                        highLightedEdge.Add(mesh.triangles[3 * hitInfo.triangleIndex + 2]);
                    }
                    else if (min == dist3) {
                        highLightedEdge.Add(mesh.triangles[3 * hitInfo.triangleIndex + 1]);
                        highLightedEdge.Add(mesh.triangles[3 * hitInfo.triangleIndex + 2]);
                    }
                    highLightedEdge.Sort();
                    if (useGLDraw) {
                        GL.Begin(GL.LINES);
                        GL.Color(Color.red);
                        GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[0]]));
                        GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[1]]));
                        GL.End();
                    }
                    else {
                        Handles.DrawSolidRectangleWithOutline(new Vector3[] {selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[0]]), 
                                                                             selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[0]]), 
                                                                             selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[1]]), 
                                                                             selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[1]])},
                                                                             Color.red, Color.red);
                    }
                }

                if (evt.type == EventType.MouseDown && highLightedEdge.Count != 0) {
                    // only select edge within 10 pix
                    bool removed = false;
                    for (int i = 0; i < selectedEdges.Count; i++) {
                        if (highLightedEdge.SequenceEqual(selectedEdges[i])) {
                            selectedEdges.RemoveAt(i);
                            removed = true;
                            break;
                        }
                    }
                    if (!Event.current.shift) {
                        selectedEdges.Clear();
                    }

                    if (!removed) {
                        highLightedEdge.Sort();
                        selectedEdges.Add(highLightedEdge);
                    }

                    handlePos = GetFacesAveragePosition(selectedEdges);
                    handleRot = selObj.transform.rotation;
                    handleScale = Vector3.one;
                }
            }
        }

        void HandleVertexSelection(Event evt) {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            RaycastHit hitInfo;
            if (Physics.Raycast(worldRay, out hitInfo) && hitInfo.collider.gameObject == selObj) {
                Vector3 vert1 = selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex]]);
                Vector3 vert2 = selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 1]]);
                Vector3 vert3 = selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * hitInfo.triangleIndex + 2]]);

                float dist1 = Vector3.Distance(hitInfo.point, vert1);
                float dist2 = Vector3.Distance(hitInfo.point, vert2);
                float dist3 = Vector3.Distance(hitInfo.point, vert3);

                float min = Mathf.Min(new float[] { dist1, dist2, dist3 });

                int highLightedVertex = -1;

                if (min < 10.0f) {
                    if (min == dist1)
                        highLightedVertex = mesh.triangles[3 * hitInfo.triangleIndex];
                    else if (min == dist2)
                        highLightedVertex = mesh.triangles[3 * hitInfo.triangleIndex + 1];
                    else if (min == dist3)
                        highLightedVertex = mesh.triangles[3 * hitInfo.triangleIndex + 2];
                    Handles.color = Color.red;
                    Handles.DotCap(2453, selObj.transform.TransformPoint(mesh.vertices[highLightedVertex]), Quaternion.identity, 0.015f);
                }

                if (evt.type == EventType.MouseDown && highLightedVertex != -1) {
                    if (!Event.current.shift) {
                        selectedVertices.Clear();
                        selectedVertices.Add(highLightedVertex);
                    }
                    else {
                        if (selectedVertices.Contains(highLightedVertex))
                            selectedVertices.Remove(highLightedVertex);
                        else
                            selectedVertices.Add(highLightedVertex);
                    }

                    handlePos = GetFaceAveragePosition(selectedVertices);
                    handleRot = selObj.transform.rotation;
                    handleScale = Vector3.one;
                }
            }
        }

        void HighlightSelectedFaces() {
            foreach (List<int> selectedFace in selectedFaces) {
                if (useGLDraw) {
                    GL.Begin(GL.TRIANGLES);
                    GL.Color(new Color(1, 1, 0, 0.5f));
                    GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[0]]));
                    GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[1]]));
                    GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[2]]));
                    GL.End();
                }
                else {
                    Handles.DrawSolidRectangleWithOutline(new Vector3[] {selObj.transform.TransformPoint(mesh.vertices[selectedFace[0]]), 
                                                                         selObj.transform.TransformPoint(mesh.vertices[selectedFace[1]]), 
                                                                         selObj.transform.TransformPoint(mesh.vertices[selectedFace[2]]), 
                                                                         selObj.transform.TransformPoint(mesh.vertices[selectedFace[2]])},
                                                                         new Color(1, 1, 0, 0.5f), new Color(0, 0, 0, 1));
                }
            }
        }

        void HandleRectSelection(Event evt) {
            if (evt.button == 0 && lmbHold) {
                //Debug.Log("LMB Up");
                if (lmbDownPos == evt.mousePosition) {
                    lmbHold = false;
                }
                else {
                    float left = Mathf.Min(lmbDownPos.x, evt.mousePosition.x);
                    float top = Mathf.Min(lmbDownPos.y, evt.mousePosition.y);
                    float width = Mathf.Abs(lmbDownPos.x - evt.mousePosition.x);
                    float height = Mathf.Abs(lmbDownPos.y - evt.mousePosition.y);

                    Rect selectionRect = new Rect(left, top, width, height);

                    if (editMode == EditMode.Vertex && mesh != null) {
                        if (!evt.shift)
                            selectedVertices.Clear();
                        for (int i = 0; i < mesh.vertices.Length; i++) {
                            if (selectionRect.Contains(HandleUtility.WorldToGUIPoint(selObj.transform.TransformPoint(mesh.vertices[i])))) {
                                if (selectedVertices.Contains(i))
                                    selectedVertices.Remove(i);
                                else
                                    selectedVertices.Add(i);
                            }
                        }
                        handlePos = GetFaceAveragePosition(selectedVertices);
                        handleRot = selObj.transform.rotation;
                        handleScale = Vector3.one;
                    }
                    else if (editMode == EditMode.Face && mesh != null) {
                        if (!evt.shift)
                            selectedFaces.Clear();
                        for (int i = 0; i < mesh.triangles.Length / 3; i++) {
                            List<int> currentFace = new List<int> { mesh.triangles[3 * i], mesh.triangles[3 * i + 1], mesh.triangles[3 * i + 2] };
                            if (selectionRect.Contains(HandleUtility.WorldToGUIPoint(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * i]]))) ||
                                selectionRect.Contains(HandleUtility.WorldToGUIPoint(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * i + 1]]))) ||
                                selectionRect.Contains(HandleUtility.WorldToGUIPoint(selObj.transform.TransformPoint(mesh.vertices[mesh.triangles[3 * i + 2]])))) {
                                if (selectedFacesIndex.Contains(i)) {
                                    selectedFacesIndex.Remove(i);

                                    for (int j = 0; j < selectedFaces.Count; j++) {
                                        if (currentFace.SequenceEqual(selectedFaces[j])) {
                                            selectedFaces.RemoveAt(j);
                                            break;
                                        }
                                    }
                                }
                                else {
                                    selectedFacesIndex.Add(i);
                                    selectedFaces.Add(currentFace);
                                }
                            }
                        }
                        handlePos = GetFacesAveragePosition(selectedFaces);
                        handleRot = selObj.transform.rotation;
                        handleScale = Vector3.one;
                    }
                    else if (editMode == EditMode.Edge && mesh != null) {
                        if (!evt.shift)
                            selectedEdges.Clear();
                        List<List<int>> addedThisRound = new List<List<int>>();
                        List<List<int>> removedThisRound = new List<List<int>>();
                        for (int i = 0; i < mesh.triangles.Length / 3; i++) {
                            List<int> edge1 = new List<int> { mesh.triangles[3 * i], mesh.triangles[3 * i + 1] };
                            List<int> edge2 = new List<int> { mesh.triangles[3 * i + 1], mesh.triangles[3 * i + 2] };
                            List<int> edge3 = new List<int> { mesh.triangles[3 * i + 2], mesh.triangles[3 * i] };

                            edge1.Sort();
                            edge2.Sort();
                            edge3.Sort();

                            List<List<int>> edges = new List<List<int>> { edge1, edge2, edge3 };

                            foreach (List<int> edge in edges) {
                                if (selectionRect.Contains(HandleUtility.WorldToGUIPoint(selObj.transform.TransformPoint(mesh.vertices[edge[0]]))) ||
                                    selectionRect.Contains(HandleUtility.WorldToGUIPoint(selObj.transform.TransformPoint(mesh.vertices[edge[1]])))) {


                                    bool removed = false;
                                    for (int j = 0; j < selectedEdges.Count; j++) {
                                        if (edge.SequenceEqual(selectedEdges[j])) {
                                            int k = 0;
                                            for (k = 0; k < addedThisRound.Count; k++) {
                                                if (edge.SequenceEqual(addedThisRound[k])) {
                                                    removed = true;
                                                    break;
                                                }
                                            }
                                            if (k == addedThisRound.Count) {
                                                selectedEdges.RemoveAt(j);
                                                removedThisRound.Add(selectedEdges[j]);
                                                removed = true;
                                            }
                                            if (removed)
                                                break;
                                        }
                                    }

                                    if (!removed) {
                                        bool hasRemoved = false;
                                        for (int j = 0; j < removedThisRound.Count; j++) {
                                            if (edge.SequenceEqual(removedThisRound[j]))
                                                hasRemoved = true;
                                        }
                                        if (!hasRemoved) {
                                            selectedEdges.Add(edge);
                                            addedThisRound.Add(edge);
                                        }
                                    }
                                }
                            }
                        }

                        handlePos = GetFacesAveragePosition(selectedEdges);
                        handleRot = selObj.transform.rotation;
                        handleScale = Vector3.one;
                    }

                    lmbHold = false;
                }
            }
        }

        void HighlightSelectedEdges() {
            foreach (List<int> selectedEdge in selectedEdges) {
                if (useGLDraw) {
                    GL.Begin(GL.LINES);
                    GL.Color(new Color(228 / 255f, 172 / 255f, 121 / 255f, 1.0f));
                    GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedEdge[0]]));
                    GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedEdge[1]]));
                    GL.End();
                }
                else {
                    Handles.DrawSolidRectangleWithOutline(new Vector3[] {selObj.transform.TransformPoint(mesh.vertices[selectedEdge[0]]), 
                                                                         selObj.transform.TransformPoint(mesh.vertices[selectedEdge[0]]), 
                                                                         selObj.transform.TransformPoint(mesh.vertices[selectedEdge[1]]), 
                                                                         selObj.transform.TransformPoint(mesh.vertices[selectedEdge[1]])},
                                                                         new Color(228 / 255f, 172 / 255f, 121 / 255f, 1.0f),
                                                                         new Color(228 / 255f, 172 / 255f, 121 / 255f, 1.0f));
                }
            }
        }

        void HighlightVertices() {
            for (int i = 0; i < mesh.vertices.Length; i++) {
                Handles.color = new Color(1, 0, 1);
                if (selectedVertices.Contains(i))
                    Handles.color = Color.yellow;
                Handles.DotCap(10 + i, selObj.transform.TransformPoint(mesh.vertices[i]), Quaternion.identity, 0.01f);
            }
        }

        void MoveVertexGroup(List<List<int>> vertexGroupList) {
            if (Event.current.type == EventType.used) return;
            Quaternion rot = new Quaternion();

            if (moveCoord == 0)
                rot = selObj.transform.rotation;
            else if (moveCoord == 1)
                rot = Quaternion.identity;
            else if (moveCoord == 2)
                rot = Quaternion.LookRotation(GetFaceNormal(vertexGroupList[0]));
            else if (moveCoord == 3) {
                rot = Quaternion.LookRotation(GetFaceNormal(vertexGroupList[0]));
                handlePos = GetFaceAveragePosition(vertexGroupList[0]);
            }


            lastHandlePos = handlePos;
            handlePos = Handles.PositionHandle(handlePos, rot);

            Vector3[] vertices = mesh.vertices;
            HashSet<int> modifiedIndex = new HashSet<int>();
            bool updated = false;
            if (lastHandlePos != handlePos) {
                foreach (List<int> face in vertexGroupList) {
                    foreach (int vertex in face) {
                        if (!modifiedIndex.Contains(vertex)) {
                            // per-face move
                            Vector3 offset = Vector3.zero;
                            if (moveCoord == 3) {
                                //offset = selObj.transform.InverseTransformDirection(Vector3.Dot((handlePos - lastHandlePos), GetFaceNormal(vertexGroupList[0])) * GetFaceNormal(face));
                                Quaternion transRot = Quaternion.FromToRotation(GetFaceNormal(vertexGroupList[0]), GetFaceNormal(face));
                                offset = selObj.transform.InverseTransformDirection(transRot * (handlePos - lastHandlePos));
                            }
                            else {
                                offset = selObj.transform.InverseTransformDirection(handlePos - lastHandlePos);
                                // if it's parent has ununiformed scale then shit
                            }
                            offset.x /= selObj.transform.localScale.x;
                            offset.y /= selObj.transform.localScale.y;
                            offset.z /= selObj.transform.localScale.z;
                            vertices[vertex] += offset;
                            modifiedIndex.Add(vertex);
                            updated = true;
                        }
                    }
                }
            }

            if (Event.current.type == EventType.used && holdingHandle == false) {
                holdingHandle = true;
                CacheUndoMeshBackup(EditType.Move);
            }
            else if (Event.current.isMouse && Event.current.button == 0 && Event.current.type != EventType.used && holdingHandle == true) {
                holdingHandle = false;
            }
            if (updated) {
                mesh.vertices = vertices;
                UpdateMeshCollider();
            }
        }

        void RotateVertexGroup(List<List<int>> vertexGroupList) {
            if (Event.current.type == EventType.used) return;
            //if (rotCoord == 0)
            //handleRot = selObj.transform.rotation;
            /*
            else if (rotCoord == 1)
                handleRot = Quaternion.identity;
            else if (rotCoord == 2)
                handleRot = Quaternion.LookRotation(GetFaceNormal(vertexGroupList[0]));
            */

            lastHandleRot = handleRot;
            handleRot = Handles.RotationHandle(handleRot, handlePos);
            //Debug.Log(handleRot);
            Vector3[] vertices = mesh.vertices;
            bool updated = false;
            HashSet<int> modifiedIndex = new HashSet<int>();
            if (lastHandleRot != handleRot) { // does not work!
                //Debug.Log("Rotate");          
                foreach (List<int> face in vertexGroupList) {
                    foreach (int vertex in face) {
                        if (!modifiedIndex.Contains(vertex)) {
                            Vector3 centerToVert = selObj.transform.TransformPoint(vertices[vertex]) - handlePos;
                            Quaternion oldToNewRot = handleRot * Quaternion.Inverse(lastHandleRot);
                            Vector3 newCenterToVert = oldToNewRot * centerToVert;
                            vertices[vertex] = selObj.transform.InverseTransformPoint(handlePos + newCenterToVert);
                            modifiedIndex.Add(vertex);
                            updated = true;
                        }
                    }
                }
            }
            if (Event.current.type == EventType.used && holdingHandle == false) {
                holdingHandle = true;
                CacheUndoMeshBackup(EditType.Rotate);
            }
            else if (Event.current.isMouse && Event.current.button == 0 && Event.current.type != EventType.used && holdingHandle == true) {
                holdingHandle = false;
            }
            if (updated) {
                mesh.vertices = vertices;
                UpdateMeshCollider();
            }
        }

        void ScaleVertexGroup(List<List<int>> vertexGroupList) {
            if (Event.current.type == EventType.used) return;
            Quaternion rot = new Quaternion();

            if (scaleCoord == 0)
                rot = selObj.transform.rotation;
            else if (scaleCoord == 1)
                rot = Quaternion.identity;
            else if (scaleCoord == 2)
                rot = Quaternion.LookRotation(GetFaceNormal(vertexGroupList[0]));

            lastHandleScale = handleScale;
            handleScale = Handles.ScaleHandle(handleScale, handlePos, rot, HandleUtility.GetHandleSize(handlePos));
            Vector3[] vertices = mesh.vertices;
            bool updated = false;


            if (lastHandleScale != handleScale) {
                HashSet<int> modifiedIndex = new HashSet<int>();
                foreach (List<int> face in vertexGroupList) {
                    foreach (int vertex in face) {
                        if (!modifiedIndex.Contains(vertex)) {
                            Vector3 centerToVert = vertices[vertex] - selObj.transform.InverseTransformPoint(handlePos);
                            centerToVert.x *= (handleScale.x / lastHandleScale.x);
                            centerToVert.y *= (handleScale.y / lastHandleScale.y);
                            centerToVert.z *= (handleScale.z / lastHandleScale.z);

                            vertices[vertex] = selObj.transform.InverseTransformPoint(handlePos) + centerToVert;
                            modifiedIndex.Add(vertex);
                            updated = true;
                        }
                    }
                }
            }
            if (Event.current.type == EventType.used && holdingHandle == false) {
                holdingHandle = true;
                CacheUndoMeshBackup(EditType.Scale);
            }
            else if (Event.current.isMouse && Event.current.button == 0 && Event.current.type != EventType.used && holdingHandle == true) {
                holdingHandle = false;
            }
            if (updated) {
                mesh.vertices = vertices;
                UpdateMeshCollider();
            }
        }

        void DrawToolBar() {
            Handles.BeginGUI();
            GUILayout.Window(2, new Rect(10, 20, 50, 50), (id) => {
                moveElement = GUILayout.Toggle(moveElement, Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/move.png", typeof(Texture)) as Texture, "Button");
                if (moveElement) {
                    rotElement = false;
                    scaleElement = false;
                }
                rotElement = GUILayout.Toggle(rotElement, Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/rotate.png", typeof(Texture)) as Texture, "Button");
                if (rotElement) {
                    moveElement = false;
                    scaleElement = false;
                }
                scaleElement = GUILayout.Toggle(scaleElement, Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/scale.png", typeof(Texture)) as Texture, "Button");
                if (scaleElement) {
                    moveElement = false;
                    rotElement = false;
                }
//                 Texture undoableIcon = Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/undo.png", typeof(Texture)) as Texture;
//                 Texture unundoableIcon = Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/unundoable.png", typeof(Texture)) as Texture;
//                 if (GUILayout.Button(meshUndoList.Count == 0 ? unundoableIcon : undoableIcon)) {
//                     UndoMeshChanges();
//                 }
            }, "Tools");

            if (moveElement) {
                GUILayout.Window(3, new Rect(80, 40, 50, 50), (subid) => {
                    GUILayout.Label("Move Axis:");
                    string[] content = { "Local", "World", "Average Normal" };
                    moveCoord = GUILayout.SelectionGrid(moveCoord, content, 3, "toggle");
                }, "Move Tool");
            }
            else if (rotElement) {
                GUILayout.Window(4, new Rect(80, 80, 50, 50), (subid) => {
                    GUILayout.Label("Rotate Axis:");
                    string[] content = { "Local" };
                    rotCoord = GUILayout.SelectionGrid(rotCoord, content, 1, "toggle");
                }, "Rotate Tool");
            }
            else if (scaleElement) {
                GUILayout.Window(5, new Rect(80, 120, 50, 50), (subid) => {
                    GUILayout.Label("Scale Axis:");
                    string[] content = { "Local", "World", "Average Normal" };
                    scaleCoord = GUILayout.SelectionGrid(scaleCoord, content, 3, "toggle");
                }, "Scale Tool");
            }
            Handles.EndGUI();
        }

        void DrawRectSelection(Event evt) {
            // draw selection rect
            if (lmbHold) {
                if (useGLDraw) {
                    GL.PushMatrix();
                    GL.LoadOrtho();
                    GL.Begin(GL.LINES);
                    GL.Color(Color.white);
                    GL.Vertex3(lmbDownPos.x / Screen.width, 1 - lmbDownPos.y / Screen.height, 0);
                    GL.Vertex3(lmbDownPos.x / Screen.width, 1 - evt.mousePosition.y / Screen.height, 0);
                    GL.Vertex3(evt.mousePosition.x / Screen.width, 1 - lmbDownPos.y / Screen.height, 0);
                    GL.Vertex3(evt.mousePosition.x / Screen.width, 1 - evt.mousePosition.y / Screen.height, 0);
                    GL.Vertex3(lmbDownPos.x / Screen.width, 1 - lmbDownPos.y / Screen.height, 0);
                    GL.Vertex3(evt.mousePosition.x / Screen.width, 1 - lmbDownPos.y / Screen.height, 0);
                    GL.Vertex3(lmbDownPos.x / Screen.width, 1 - evt.mousePosition.y / Screen.height, 0);
                    GL.Vertex3(evt.mousePosition.x / Screen.width, 1 - evt.mousePosition.y / Screen.height, 0);
                    GL.End();
                    GL.PopMatrix();
                }
                else {
                    Handles.BeginGUI();
                    GUI.Box(new Rect(Mathf.Min(lmbDownPos.x, evt.mousePosition.x), Mathf.Min(lmbDownPos.y, evt.mousePosition.y), Mathf.Abs(evt.mousePosition.x - lmbDownPos.x), Mathf.Abs(evt.mousePosition.y - lmbDownPos.y)), "");
                    Handles.EndGUI();
                }
            }
        }

        void DrawFastSel(Event evt) {
            if (useGLDraw) {
                GL.PushMatrix();
                GL.LoadOrtho();
                GL.Begin(GL.LINES);
                GL.Color(Color.white);
                GL.Vertex(new Vector3(rmbMousePos.x / Screen.width, 1 - (rmbMousePos.y + 25) / Screen.height, 0));
                GL.Vertex(new Vector3(evt.mousePosition.x / Screen.width, 1 - (evt.mousePosition.y + 25) / Screen.height, 0));
                GL.End();
                GL.PopMatrix();
            }
            else {

                if (Camera.current == null) return;

                Vector3 worldRmbPos = Camera.current.ScreenToWorldPoint(new Vector3(rmbMousePos.x, Screen.height - rmbMousePos.y - 36, Camera.current.nearClipPlane + 0.001f));
                Vector3 worldEvtPos = Camera.current.ScreenToWorldPoint(new Vector3(evt.mousePosition.x, Screen.height - evt.mousePosition.y - 36, Camera.current.nearClipPlane + 0.001f));

                Handles.DrawLine(worldRmbPos, worldEvtPos);
            }
            HandleUtility.Repaint();

            Handles.BeginGUI();
            GUILayout.Window(6, new Rect(rmbMousePos.x - 100, rmbMousePos.y, 50, 20), (subid) => { GUILayout.Label("Vertex"); }, " ");
            GUILayout.Window(7, new Rect(rmbMousePos.x, rmbMousePos.y - 50, 50, 20), (subid) => { GUILayout.Label("Edge"); }, " ");
            GUILayout.Window(8, new Rect(rmbMousePos.x + 100, rmbMousePos.y, 50, 20), (subid) => { GUILayout.Label("Object"); }, " ");
            GUILayout.Window(9, new Rect(rmbMousePos.x, rmbMousePos.y + 50, 50, 20), (subid) => { GUILayout.Label("Face"); }, " ");
            Handles.EndGUI();
        }

        void HandleFastSel(Event evt) {
            if (evt.type == EventType.MouseDown && !evt.alt) {
                if (evt.button == 1) {
                    if (rmbHold == false) {
                        rmbMousePos = evt.mousePosition;
                        rmbHold = true;
                        evt.Use();
                    }
                }
            }
            else if (evt.type == EventType.MouseUp) {
                if (evt.button == 1 && rmbHold == true) {
                    rmbHold = false;

                    Rect vertexRect = new Rect(rmbMousePos.x - 100, rmbMousePos.y, 50, 20);
                    Rect edgeRect = new Rect(rmbMousePos.x, rmbMousePos.y - 50, 50, 20);
                    Rect objRect = new Rect(rmbMousePos.x + 100, rmbMousePos.y, 50, 20);
                    Rect faceRect = new Rect(rmbMousePos.x, rmbMousePos.y + 50, 50, 20);

                    if (vertexRect.Contains(evt.mousePosition)) {
                        if (editMode == EditMode.Object)
                            MeshEditMode();
                        editMode = EditMode.Vertex;
                    }
                    else if (edgeRect.Contains(evt.mousePosition)) {
                        if (editMode == EditMode.Object)
                            MeshEditMode();
                        editMode = EditMode.Edge;
                    }
                    else if (faceRect.Contains(evt.mousePosition)) {
                        if (editMode == EditMode.Object)
                            MeshEditMode();
                        editMode = EditMode.Face;
                    }
                    else if (objRect.Contains(evt.mousePosition)) {
                        ExitMeshEditMode();
                        editMode = EditMode.Object;
                    }
                    HandleUtility.Repaint();
                    Repaint();
                }
            }

        }

        void HandleHotKey(Event e) {
            if (e.keyCode == KeyCode.Q) {
                moveElement = false;
                rotElement = false;
                scaleElement = false;
                e.Use();
            }
            else if (e.keyCode == KeyCode.W) {
                moveElement = true;
                rotElement = false;
                scaleElement = false;
                if (moveCoord == 3) moveCoord = 0;
                e.Use();
            }
            else if (e.keyCode == KeyCode.E) {
                moveElement = false;
                rotElement = true;
                scaleElement = false;
                e.Use();
            }
            else if (e.keyCode == KeyCode.R) {
                moveElement = false;
                rotElement = false;
                scaleElement = true;
                e.Use();
            }
        }

        Vector3 GetFacesAveragePosition(List<List<int>> faces) {
            Vector3 pos = Vector3.zero;
            foreach (List<int> face in faces) {
                pos += GetFaceAveragePosition(face);
            }
            return pos / faces.Count;
        }

        Vector3 GetFaceAveragePosition(List<int> face) {
            Vector3 result = Vector3.zero;
            foreach (int idx in face) {
                result += selObj.transform.TransformPoint(mesh.vertices[idx]);
            }
            return result / face.Count;
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

            return Vector3.Cross(edge1.normalized, edge2.normalized).normalized;
        }

        void UpdateMeshCollider() {
            selObj.GetComponent<MeshCollider>().sharedMesh = null;
            selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        void CacheUndoMeshBackup(EditType type) {
            Undo.RegisterUndo(mesh, "Mesh Changed");
            Mesh meshBackup/* = new Mesh()*/;
            meshBackup = (Mesh)Instantiate(mesh);
            meshBackup.name = mesh.name;
            if (meshUndoList.Count >= 10)
                meshUndoList.RemoveAt(0);
            KeyValuePair<Mesh, EditType> pair = new KeyValuePair<Mesh, EditType>(meshBackup, type);
            meshUndoList.Add(pair);
        }

        void UndoMeshChanges() {
            if (meshUndoList.Count == 0) return;
            KeyValuePair<Mesh, EditType> undoPair = meshUndoList[meshUndoList.Count - 1];
            Mesh undoMeshBackup = undoPair.Key;
            meshUndoList.RemoveAt(meshUndoList.Count - 1);
            mesh = /*(Mesh)Instantiate(*/undoMeshBackup/*)*/;
            //mesh.name = undoMeshBackup.name;
            selObj.GetComponent<MeshFilter>().sharedMesh = mesh;
            selObj.GetComponent<MeshRenderer>().sharedMaterials = selObj.GetComponent<MeshRenderer>().sharedMaterials.Take(undoMeshBackup.subMeshCount).ToArray();

            handleRot = selObj.transform.rotation;
            handleScale = Vector3.one;

            if (undoPair.Value == EditType.ChangeMat ||
                undoPair.Value == EditType.Harden ||
                undoPair.Value == EditType.Extrude ||
                undoPair.Value == EditType.DelFace ) {
                selectedFaces.Clear();
                selectedFacesIndex.Clear();
                selectedVertices.Clear();
                selectedEdges.Clear();
            }
            else {
                if (selectedFaces.Count != 0)
                    handlePos = GetFacesAveragePosition(selectedFaces);
                else if (selectedEdges.Count != 0)
                    handlePos = GetFacesAveragePosition(selectedEdges);
                else if (selectedVertices.Count != 0)
                    handlePos = GetFaceAveragePosition(selectedVertices);
                else
                    handlePos = selObj.transform.position;
            }
            UpdateMeshCollider();
            //DestroyImmediate(undoMeshBackup);
            Debug.Log("Undo " + undoPair.Value);
        }

        void ChangeMaterial() {
            if (selectedFaces.Count == 0) {
                Debug.LogError("No Face Selected!");
                return;
            }

            realTriangleArrayWithSubMeshSeparated.Clear();
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

            CacheUndoMeshBackup(EditType.ChangeMat);
            int subMeshIdx = 0;
            foreach (List<List<int>> realTriangleArray in realTriangleArrayWithSubMeshSeparated) {
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

                mesh.SetTriangles(newTriangleList.ToArray(), subMeshIdx);
                subMeshIdx++;
            }
            mesh.subMeshCount += 1;

            List<int> selectedTriangleList = new List<int>();
            foreach (List<int> selectedFace in selectedFaces) {
                selectedTriangleList.AddRange(selectedFace);
            }
            mesh.SetTriangles(selectedTriangleList.ToArray(), mesh.subMeshCount - 1);

            //AssetDatabase.CreateAsset(newMesh, "Assets/Mesh/" + selObj.name);
            //AssetDatabase.Refresh();

            List<Material> materials = new List<Material>();
            materials.AddRange(selObj.renderer.sharedMaterials);

            if (!createNewMaterial && assignedMat != null) {
                materials.Add(assignedMat);

            }
            else {
                Material newMat = new Material(Shader.Find("Diffuse"));
                AssetDatabase.CreateAsset(newMat, materialPath + "/newMat_" + mesh.subMeshCount + ".mat");
                AssetDatabase.Refresh();
                newMat.color = Color.blue;

                materials.Add(newMat);
            }

            mesh.Optimize();

            selObj.GetComponent<MeshFilter>().sharedMesh = mesh;
            selObj.renderer.sharedMaterials = materials.ToArray();

            selObj.GetComponent<MeshCollider>().sharedMesh = mesh;

            realTriangleArrayWithSubMeshSeparated.Clear();
            selectedFaces.Clear();
            selectedFacesIndex.Clear();
            UpdateMeshCollider();
            //editMode = EditMode.Object;
            //ExitMeshEditMode();
            //Selection.objects = new UnityEngine.Object[0];
        }

        void ClearUndoList() {
            for (int i = 0; i < meshUndoList.Count; i++) {
                DestroyImmediate(meshUndoList[i].Key);
            }
            meshUndoList.Clear();
        }

        void CheckMeshReferenceCount() {
            MeshFilter[] allMeshFilter = FindObjectsOfType<MeshFilter>();

            foreach (MeshFilter meshFilter in allMeshFilter) {
                if (meshReferenceCount.ContainsKey(meshFilter.sharedMesh)) {
                    meshReferenceCount[meshFilter.sharedMesh]++;
                }
                else
                    meshReferenceCount.Add(meshFilter.sharedMesh, 1);
            }
        }

        void OnEnable() {
            SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
            SceneView.onSceneGUIDelegate += this.OnSceneGUI;
        }

        void OnDestroy() {
            ExitMeshEditMode();
            SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
			Tools.current = Tool.Move;
        }
    }
}