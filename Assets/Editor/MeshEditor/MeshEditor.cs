using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System;

public class MeshEditor : EditorWindow {
    bool createNewMaterial = true;
    bool moveElement = false;
    bool rotElement = false;
    bool scaleElement = false;
    bool hasMeshCollider = false;
    bool rmbHold = false;
    bool lmbHold = false;
    bool keepFaceTogether = false;
    bool editOnOriginalMesh = false;
    bool holdingHandle = false;
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
    Material assignedMat = null;
    List<Mesh> meshUndoList = new List<Mesh>(10);

    EditMode editMode = EditMode.Object;

    enum EditMode {
        Object  = 0,
        Vertex  = 1,
        Edge    = 2,
        Face    = 3
    }

    [MenuItem("Mesh Editor/Mesh Editor Panel")]
    public static void ShowWindow() {
        EditorWindow window = EditorWindow.GetWindow(typeof(MeshEditor));
        window.minSize = new Vector2(350, 350);
    }

    void OnGUI() {
        GUILayout.BeginVertical();
        {
            EditorGUILayout.LabelField("Mesh Editor 1.0", EditorStyles.boldLabel);
            EditMode newMode = (EditMode)EditorGUILayout.EnumPopup("Edit Mode", editMode);
            if (newMode != editMode) {
                if (editMode == EditMode.Object)
                    MeshEditMode();
                editMode = newMode;
            }
            editOnOriginalMesh = GUILayout.Toggle(editOnOriginalMesh, "Edit On Original Mesh");
            GUILayout.Label("Notice: Mesh Editor will NOT modify the source file (e.g. \n" +
                            "*.fbx) but just the imported mesh. Reimport the asset will \n" +
                            "revert all changes to the mesh.\n"+
                            "Select \"Edit On Original Mesh\" will affect all instances\n" +
                            "in project, otherwise a new mesh copy will be create.", GUILayout.Width(400));
            GUILayout.Space(20);

            EditorGUILayout.LabelField("Face Editing Tools", EditorStyles.boldLabel);
            GUILayout.Label("Change material for selected faces");
            createNewMaterial = GUILayout.Toggle(createNewMaterial, "Create New Material");
            if (createNewMaterial) {

            }
            else {
                assignedMat = EditorGUILayout.ObjectField("Material to use", assignedMat, typeof(Material), true) as Material;
            }
            if (GUILayout.Button("Change Material", GUILayout.ExpandWidth(false))) {
                ChangeMaterial();
            }
            GUILayout.Space(10);
            GUILayout.Label("Extrude Selected Faces");
            keepFaceTogether = GUILayout.Toggle(keepFaceTogether, "Keep face together");
            if (GUILayout.Button("Extrude", GUILayout.ExpandWidth(false))) {
                Extrude();
            }
            GUILayout.Space(10);
            /*
            if (GUILayout.Button("Recalculate Normals")) {
                if (mesh) {
                    mesh.RecalculateNormals();
                }
            }
            */
        }
        GUILayout.EndVertical();
    }

    void MeshEditMode() {
        ExitMeshEditMode();
        selObj = Selection.activeGameObject;
        if (editOnOriginalMesh)
            mesh = selObj.GetComponent<MeshFilter>().sharedMesh;
        else {
            mesh = new Mesh();
            mesh = (Mesh)Instantiate(selObj.GetComponent<MeshFilter>().sharedMesh);
            selObj.GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        if (selObj.GetComponent<MeshCollider>() == null) {
            MeshCollider mc = selObj.AddComponent<MeshCollider>();
            mc.sharedMesh = selObj.GetComponent<MeshFilter>().sharedMesh;
            hasMeshCollider = false;
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
            boxCollider.enabled = true;
        }

        foreach (SphereCollider sphereCollider in sColliders) {
            sphereCollider.enabled = true;
        }

        foreach (CapsuleCollider capsuleCollider in cColliders) {
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
    }

    void OnSceneGUI(SceneView scnView) {
        Event evt = Event.current;
        DrawToolBar();

        if (Selection.activeGameObject == null && editMode != EditMode.Object) {
            ExitMeshEditMode();
            editMode = EditMode.Object;
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

    // check if this really works!!!
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

    void Extrude() {
        List<Vector3> vertexList = new List<Vector3>(mesh.vertices);
        List<Vector2> uvList = new List<Vector2>(mesh.uv);
        List<Vector3> normalList = new List<Vector3>(mesh.normals);
        List<int> triangleList = new List<int>(mesh.triangles);

        List<List<int>> extrudedFaces = new List<List<int>>();
        List<int> extrudedFacesIndex = new List<int>();

        CacheUndoMeshBackup(mesh);
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
    }

    void HandleFaceSelection(Event evt) {
        Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        RaycastHit hitInfo;
        if (Physics.Raycast(worldRay, out hitInfo) && hitInfo.collider.gameObject == selObj) {
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
                    highLightedEdge.Add(Mathf.Min(mesh.triangles[3 * hitInfo.triangleIndex], mesh.triangles[3 * hitInfo.triangleIndex + 1]));
                    highLightedEdge.Add(Mathf.Max(mesh.triangles[3 * hitInfo.triangleIndex], mesh.triangles[3 * hitInfo.triangleIndex + 1]));
                }
                else if (min == dist2) {
                    highLightedEdge.Add(Mathf.Min(mesh.triangles[3 * hitInfo.triangleIndex], mesh.triangles[3 * hitInfo.triangleIndex + 2]));
                    highLightedEdge.Add(Mathf.Max(mesh.triangles[3 * hitInfo.triangleIndex], mesh.triangles[3 * hitInfo.triangleIndex + 2]));
                }
                else if (min == dist3) {
                    highLightedEdge.Add(Mathf.Min(mesh.triangles[3 * hitInfo.triangleIndex + 1], mesh.triangles[3 * hitInfo.triangleIndex + 2]));
                    highLightedEdge.Add(Mathf.Max(mesh.triangles[3 * hitInfo.triangleIndex + 1], mesh.triangles[3 * hitInfo.triangleIndex + 2]));
                }

                GL.Begin(GL.LINES);
                GL.Color(Color.red);
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[0]]));
                GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[highLightedEdge[1]]));
                GL.End();
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
                Handles.DotCap(2453, selObj.transform.TransformPoint(mesh.vertices[highLightedVertex]), Quaternion.identity, 0.05f);
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
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1, 1, 0, 0.5f));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[0]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[1]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedFace[2]]));
            GL.End();
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
            GL.Begin(GL.LINES);
            GL.Color(new Color(228/255f, 172/255f, 121/255f, 1.0f));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedEdge[0]]));
            GL.Vertex(selObj.transform.TransformPoint(mesh.vertices[selectedEdge[1]]));
            GL.End();
        }
    }

    void HighlightVertices() {
        for (int i = 0; i < mesh.vertices.Length; i++) {
            Handles.color = new Color(1, 0, 1);
            if (selectedVertices.Contains(i))
                Handles.color = Color.yellow;
            Handles.DotCap(10 + i, selObj.transform.TransformPoint(mesh.vertices[i]), Quaternion.identity, 0.03f);
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
                        //if (moveCoord == 2)
                        //    vertices[vertex] += selObj.transform.InverseTransformDirection((handlePos - lastHandlePos).magnitude * GetFaceNormal(face));
                        //else 
                            vertices[vertex] += selObj.transform.InverseTransformDirection(handlePos - lastHandlePos);
                        modifiedIndex.Add(vertex);
                        updated = true;
                    }
                }
            }
        } 
        
        if (Event.current.type == EventType.used && holdingHandle == false) {
            holdingHandle = true;
            CacheUndoMeshBackup(mesh);
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
            CacheUndoMeshBackup(mesh);
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
            CacheUndoMeshBackup(mesh);
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
            Texture undoableIcon = Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/undo.png", typeof(Texture)) as Texture;
            Texture unundoableIcon = Resources.LoadAssetAtPath("Assets/Editor/MeshEditor/MeshEditorUI/unundoable.png", typeof(Texture)) as Texture;
            if (GUILayout.Button(meshUndoList.Count == 0 ? unundoableIcon: undoableIcon)) {
                UndoMeshChanges();
            }
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
    }

    void DrawFastSel(Event evt) {
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
        GUILayout.Window(8, new Rect(rmbMousePos.x + 100, rmbMousePos.y, 50, 20), (subid) => { GUILayout.Label("Object"); }, " ");
        GUILayout.Window(9, new Rect(rmbMousePos.x, rmbMousePos.y + 50, 50, 20), (subid) => { GUILayout.Label("Face"); }, " ");
        Handles.EndGUI();
    }

    void HandleFastSel(Event evt) {
        if (evt.type == EventType.MouseDown) {
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

        return Vector3.Cross(edge1, edge2).normalized;
    }

    void UpdateMeshCollider() {
        selObj.GetComponent<MeshCollider>().sharedMesh = null;
        selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void CacheUndoMeshBackup(Mesh oldMesh) {
        //Debug.Log("Saved Old Mesh");
        Mesh meshBackup = new Mesh();
        meshBackup = (Mesh)Instantiate(oldMesh);
        meshBackup.name = oldMesh.name;
        if (meshUndoList.Count >= 10)
            meshUndoList.RemoveAt(0);
        meshUndoList.Add(meshBackup);
    }

    void UndoMeshChanges() {
        if (meshUndoList.Count == 0) return;
        Mesh undoMeshBackup = meshUndoList[meshUndoList.Count - 1];
        meshUndoList.RemoveAt(meshUndoList.Count - 1);
        mesh = (Mesh)Instantiate(undoMeshBackup);
        mesh.name = undoMeshBackup.name;
        selObj.GetComponent<MeshFilter>().sharedMesh = mesh;
        UpdateMeshCollider();

        handleRot = selObj.transform.rotation;
        handleScale = Vector3.one;
        undoMeshBackup = null;
        selectedFaces.Clear();
        selectedFacesIndex.Clear();
        selectedVertices.Clear();
        selectedEdges.Clear();
        UpdateMeshCollider();
        Debug.Log("Undo");
    }

    void ChangeMaterial() {
        if (selectedFaces.Count == 0) {
            Debug.LogError("No Face Selected!");
            return;
        }

        //CacheUndoMeshBackup(mesh);
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
            AssetDatabase.CreateAsset(newMat, "Assets/Material/newMat_" + mesh.subMeshCount + ".mat");
            AssetDatabase.Refresh();
            newMat.color = Color.blue;

            materials.Add(newMat);
        }

        mesh.Optimize();

        selObj.GetComponent<MeshFilter>().sharedMesh = mesh;
        selObj.renderer.sharedMaterials = materials.ToArray();

        if (hasMeshCollider)
            selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
        else
            DestroyImmediate(selObj.GetComponent<MeshCollider>());
        realTriangleArrayWithSubMeshSeparated.Clear();
        selectedFaces.Clear();
        selectedFacesIndex.Clear();
        UpdateMeshCollider();
        //editMode = EditMode.Object;
        //ExitMeshEditMode();
        //Selection.objects = new UnityEngine.Object[0];
    }

    void OnEnable() {
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
        SceneView.onSceneGUIDelegate += this.OnSceneGUI;
    }

    void OnDestroy() {
        ExitMeshEditMode();
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
    }
}