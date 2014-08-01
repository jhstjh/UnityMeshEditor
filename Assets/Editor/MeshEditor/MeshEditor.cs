using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System;

//[ExecuteInEditMode]
public class MeshEditor : EditorWindow {
    bool skinedMesh = true;
    bool createNewMaterial = true;
    bool moveElement = false;
    bool rotElement = false;
    bool scaleElement = false;
    bool hasMeshCollider = false;
    bool succeed = false;
    bool prepared = false;
    bool rmbHold = false;
    bool keepFaceTogether = false;
    Vector2 rmbMousePos;

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
    Dictionary<int, int> vertexMapping = new Dictionary<int, int>();
    Dictionary<HashSet<int>, int> edgeOccurance = new Dictionary<HashSet<int>, int>(new HashSetEqualityComparer<int>());
    Material assignedMat = null;

    EditMode editMode = EditMode.Object;

    enum EditMode {
        Object  = 0,
        Vertex  = 1,
        Edge    = 2,
        Face    = 3
    }

    [MenuItem("Mesh Editor/Mesh Editor Panel")]
    public static void ShowWindow() {
        EditorWindow.GetWindow(typeof(MeshEditor));
    }

    void OnGUI() {
        GUILayout.BeginVertical();
        {
            GUILayout.Label("Mesh Editor");
            GUILayout.BeginHorizontal();

            editMode = (EditMode)EditorGUILayout.EnumPopup("Edit Mode", editMode);
            GUILayout.EndHorizontal();
            GUILayout.Label("Notice: \nMesh Editor will NOT modify the source file (e.g. \n*.fbx) but just the imported mesh. Reimport\n the asset will revert all changes to the mesh.", GUILayout.Width(300));
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
            if (GUILayout.Button("Extrude")) {
                Extrude();
            }
            keepFaceTogether = GUILayout.Toggle(keepFaceTogether, "Keep face together");
            GUILayout.EndVertical();
        }
        GUILayout.EndVertical();
    }

    void MeshEditMode() {
        selObj = Selection.activeGameObject;
        mesh = selObj.GetComponent<MeshFilter>().sharedMesh;

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
    }

    void ExitMeshEditMode() {
        if (hasMeshCollider) {
            selObj.GetComponent<MeshCollider>().sharedMesh = null;
            selObj.GetComponent<MeshCollider>().sharedMesh = mesh;
        }
        else
            DestroyImmediate(selObj.GetComponent<MeshCollider>());
        mesh.Optimize();
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

        HandleFastSel(evt);
        if (rmbHold) {
            DrawFastSel(evt);
        }

        if (editMode == EditMode.Face && mesh != null) {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (evt.isKey) 
                HandleHotKey(evt);
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
            HighlightVertices();
            if (moveElement && selectedVertices.Count != 0) 
                MoveVertex();
            if (rotElement && selectedVertices.Count != 0) 
                RotateVertex();
            if (scaleElement && selectedVertices.Count != 0) 
                ScaleVertex();
            HandleVertexSelection(Event.current);
            HandleUtility.Repaint();
        }
        else if (editMode == EditMode.Edge && mesh != null) {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (evt.isKey) 
                HandleHotKey(evt);
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
                //selectedFacesIndex.Add(hitInfo.triangleIndex);

                bool removed = false;
                for (int i = 0; i < selectedFaces.Count; i++) {
                    // if it already exists, remove it from selection, not working? TODO
                    if (selectedFace.SequenceEqual(selectedFaces[i])) {
                        selectedFaces.RemoveAt(i);
                        selectedFacesIndex.Remove(hitInfo.triangleIndex);
                        removed = true;
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
                    // if it already exists, remove it from selection, not working? TODO
                    if (highLightedEdge.SequenceEqual(selectedEdges[i])) {
                        selectedEdges.RemoveAt(i);
                        removed = true;
                    }
                }
                if (!Event.current.shift) {
                    selectedEdges.Clear();
                }

                if (!removed)
                    selectedEdges.Add(highLightedEdge);

                handlePos = GetFacesAveragePosition(selectedEdges);
                handleRot = selObj.transform.rotation;
                handleScale = Vector3.one;
            }
        }
    }

    void HandleVertexSelection(Event evt) {
#if true
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
                //int vertIdx = vertObj.IndexOf(hitInfo.collider.gameObject);

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
#else
        Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        RaycastHit hitInfo;
        if (Physics.Raycast(worldRay, out hitInfo) && hitInfo.collider.gameObject.name.Contains("VertHelper_")) {

            /*
            Vector2 vertScreenPos = HandleUtility.WorldToGUIPoint(hitInfo.collider.gameObject.transform.position);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.Color(Color.red);
            GL.Vertex(new Vector3((vertScreenPos.x - 2) / Screen.width, 1 - (vertScreenPos.y - 2) / Screen.height, 0));
            GL.Vertex(new Vector3((vertScreenPos.x + 2) / Screen.width, 1 - (vertScreenPos.y - 2) / Screen.height, 0));
            GL.Vertex(new Vector3((vertScreenPos.x + 2) / Screen.width, 1 - (vertScreenPos.y + 2) / Screen.height, 0));
            GL.Vertex(new Vector3((vertScreenPos.x - 2) / Screen.width, 1 - (vertScreenPos.y + 2) / Screen.height, 0));
            GL.End();
            GL.PopMatrix();
            */

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (evt.type == EventType.MouseDown) {
                int vertIdx = vertObj.IndexOf(hitInfo.collider.gameObject);

                if (selectedVertices.Contains(vertIdx))
                    selectedVertices.Remove(vertIdx);
                else {
                    if (!Event.current.shift) {
                        selectedVertices.Clear();
                    }
                    selectedVertices.Add(vertIdx);
                }

                handlePos = GetFaceAveragePosition(selectedVertices);
                handleRot = selObj.transform.rotation;
                handleScale = Vector3.one;
            }
        }
#endif
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
        Quaternion rot = new Quaternion();

        if (moveCoord == 0)
            rot = selObj.transform.rotation;
        else if (moveCoord == 1)
            rot = Quaternion.identity;
        else if (moveCoord == 2)
            rot = Quaternion.LookRotation(GetFaceNormal(vertexGroupList[0]));

        lastHandlePos = handlePos;
        handlePos = Handles.PositionHandle(handlePos, rot);

        HashSet<int> modifiedIndex = new HashSet<int>();
        if (lastHandlePos != handlePos) {
            Vector3[] vertices = mesh.vertices;
            foreach (List<int> face in vertexGroupList) {
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

    void RotateVertexGroup(List<List<int>> vertexGroupList) {
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
            foreach (List<int> face in vertexGroupList) {
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

    void ScaleVertexGroup(List<List<int>> vertexGroupList) {
        Quaternion rot = new Quaternion();

        if (scaleCoord == 0)
            rot = selObj.transform.rotation;
        else if (scaleCoord == 1)
            rot = Quaternion.identity;
        else if (scaleCoord == 2)
            rot = Quaternion.LookRotation(GetFaceNormal(vertexGroupList[0]));

        lastHandleScale = handleScale;
        handleScale = Handles.ScaleHandle(handleScale, handlePos, rot, 2.5f);
        //Debug.Log(handleScale);

        if (lastHandleScale != handleScale) {
            HashSet<int> modifiedIndex = new HashSet<int>();

            Vector3[] vertices = mesh.vertices;
            foreach (List<int> face in vertexGroupList) {
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

    void MoveVertex() {

        Quaternion rot = new Quaternion();

        if (moveCoord == 0)
            rot = selObj.transform.rotation;
        else if (moveCoord == 1)
            rot = Quaternion.identity;
        else if (moveCoord == 2)
            rot = Quaternion.LookRotation(mesh.normals[selectedVertices[0]]);

        lastHandlePos = handlePos;
        handlePos = Handles.PositionHandle(handlePos, rot);

        //Debug.Log(handlePos);

        HashSet<int> modifiedIndex = new HashSet<int>();
        if (lastHandlePos != handlePos) {
            Vector3[] vertices = mesh.vertices;
            foreach (int vertex in selectedVertices) {
                if (!modifiedIndex.Contains(vertex)) {
                    vertices[vertex] += selObj.transform.InverseTransformDirection(handlePos - lastHandlePos);
                    //vertObj[vertex].transform.position = selObj.transform.TransformPoint(vertices[vertex]);
                    modifiedIndex.Add(vertex);
                }
            }

            mesh.vertices = vertices;
            UpdateMeshCollider();
        }
    }

    void ScaleVertex() {
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

            foreach (int vertex in selectedVertices) {
                if (!modifiedIndex.Contains(vertex)) {
                    Vector3 centerToVert = vertices[vertex] - selObj.transform.InverseTransformPoint(handlePos);
                    centerToVert.x *= (handleScale.x / lastHandleScale.x);
                    centerToVert.y *= (handleScale.y / lastHandleScale.y);
                    centerToVert.z *= (handleScale.z / lastHandleScale.z);

                    vertices[vertex] = selObj.transform.InverseTransformPoint(handlePos) + centerToVert;
                    modifiedIndex.Add(vertex);
                }
            }

            mesh.vertices = vertices;
            UpdateMeshCollider();
        }
    }

    void RotateVertex() {
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
            foreach (int vertex in selectedVertices) {
                if (!modifiedIndex.Contains(vertex)) {
                    Vector3 centerToVert = selObj.transform.TransformPoint(vertices[vertex]) - handlePos;
                    Quaternion oldToNewRot = handleRot * Quaternion.Inverse(lastHandleRot);
                    Vector3 newCenterToVert = oldToNewRot * centerToVert;
                    vertices[vertex] = selObj.transform.InverseTransformPoint(handlePos + newCenterToVert);
                    modifiedIndex.Add(vertex);
                }
            }
            mesh.vertices = vertices;
            UpdateMeshCollider();
        }
    }

    void DrawToolBar() {
        Handles.BeginGUI();
        GUILayout.Window(2, new Rect(10, 20, 50, 50), (id) => {
            moveElement = GUILayout.Toggle(moveElement, EditorGUIUtility.Load("MeshEditor/move.png") as Texture, "Button");
            if (moveElement) {
                rotElement = false;
                scaleElement = false;
            }
            rotElement = GUILayout.Toggle(rotElement, EditorGUIUtility.Load("MeshEditor/rotate.png") as Texture, "Button");
            if (rotElement) {
                moveElement = false;
                scaleElement = false;
            }
            scaleElement = GUILayout.Toggle(scaleElement, EditorGUIUtility.Load("MeshEditor/scale.png") as Texture, "Button");
            if (scaleElement) {
                moveElement = false;
                rotElement = false;
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
                string[] content = { "Local", "World", "Average Normal" };
                rotCoord = GUILayout.SelectionGrid(rotCoord, content, 3, "toggle");
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
                }
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
        //return (selObj.transform.TransformPoint(mesh.vertices[face[0]]) + selObj.transform.TransformPoint(mesh.vertices[face[1]]) + selObj.transform.TransformPoint(mesh.vertices[face[2]])) / 3;
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

    void ChangeMaterial() {
        if (selectedFaces.Count == 0) {
            Debug.LogError("No Face Selected!");
            return;
        }

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

    void OnEnable() {
        //MeshEditMode();
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
        SceneView.onSceneGUIDelegate += this.OnSceneGUI;
    }

    void OnDestroy() {
        SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
    }
}