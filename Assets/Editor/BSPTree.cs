using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;


public class BSPWindow : Editor {

    static bool hasfinished = false;
    static BSPTree btree;
    [MenuItem("BSPTree/CreateBSPTree")]
    public static void ShowWindow() {
        SceneView.onSceneGUIDelegate -= OnSceneGUI;
        SceneView.onSceneGUIDelegate += OnSceneGUI;
        //BSPTree tree = new BSPTree(Selection.activeGameObject, Selection.activeGameObject.GetComponent<MeshFilter>().sharedMesh);
        btree = ScriptableObject.CreateInstance(typeof(BSPTree)) as BSPTree;
        btree.Init(Selection.activeGameObject, Selection.activeGameObject.GetComponent<MeshFilter>().sharedMesh);
        hasfinished = true;
    }

    void OnEnable() {
        //SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
        //SceneView.onSceneGUIDelegate += this.OnSceneGUI;
    }

    void OnDestroy() {
        //SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
    }

    static void OnSceneGUI(SceneView scnView) {
        if (hasfinished == true) {
            DrawNode(btree, btree.root);
            HandleUtility.Repaint();

        }
    }

    static void DrawNode(BSPTree tree, BSPNode node) {
        //Debug.Log("aaaaaa");
        GL.Begin(GL.TRIANGLES);
        GL.Color(new Color(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), 0.5f));
        GL.Vertex(tree.toWorldCoord(node.node.indices[0]) + Vector3.up * 5);
        GL.Vertex(tree.toWorldCoord(node.node.indices[1]) + Vector3.up * 5);
        GL.Vertex(tree.toWorldCoord(node.node.indices[2]) + Vector3.up * 5);
        GL.End();
        if (node.frontNode != null)
            DrawNode(tree, node.frontNode);
        if (node.backNode != null)
            DrawNode(tree, node.backNode);
    }

}

/// <summary>
/// All vertices convert to world coordinate for intersection testing
/// </summary>
public class BSPTree : Editor{


    enum FaceRelation {
        Front,
        Back,
        Intersect
    }

    List<Plane> planeEquations;
    public BSPNode root;
    Mesh mesh;
    public GameObject gameObj;
    List<int> triangles;
    List<Vector3> vertices;

    public void Init(GameObject pGameObj, Mesh pMesh) {
        gameObj = pGameObj;
        mesh = pMesh;

        triangles = new List<int>(mesh.triangles);
        vertices = new List<Vector3>(mesh.vertices);
        planeEquations = new List<Plane>();

        CalcPlaneEquations();
        
        root = new BSPNode();
        root.subtreeNodes = planeEquations;

        ConstructTree(root);
    }
    
    void CalcPlaneEquations() {
        for (int i = 0; i < triangles.Count ; i += 3) {
            Vector3 normal = GetFaceNormal(new List<int> { triangles[i], triangles[i + 1], triangles[i + 2] });
            Plane p = new Plane(normal.x, normal.y, normal.z, -Vector3.Dot(normal, gameObj.transform.TransformPoint(vertices[triangles[i]])),
                                new List<int> { triangles[i], triangles[i + 1], triangles[i + 2] }, i/3);
            planeEquations.Add(p);
        }
    }

    void ConstructTree(BSPNode theRoot) {
        theRoot.node = theRoot.subtreeNodes[0];
        for (int i = 1; i < theRoot.subtreeNodes.Count; i++) {
            FaceRelation relation = GetFaceRelation(theRoot.node, theRoot.subtreeNodes[i].indices);
            if (relation == FaceRelation.Front) {
                if (theRoot.frontNode == null) theRoot.frontNode = new BSPNode();
                theRoot.frontNode.subtreeNodes.Add(theRoot.subtreeNodes[i]);
            }
            else if (relation == FaceRelation.Back) {
                if (theRoot.backNode == null) theRoot.backNode = new BSPNode();
                theRoot.backNode.subtreeNodes.Add(theRoot.subtreeNodes[i]);
            }
            else if (relation == FaceRelation.Intersect) {
                // TODO
            }
        }
        theRoot.subtreeNodes.Clear();
        if (theRoot.frontNode != null && theRoot.frontNode.subtreeNodes.Count != 0)
            ConstructTree(theRoot.frontNode);
        if (theRoot.backNode != null && theRoot.backNode.subtreeNodes.Count != 0)
            ConstructTree(theRoot.backNode);
    }

    List<List<int>> splitTriangle(Plane plane, List<int> triangle) {
        List<List<int>> result = new List<List<int>>();
        List<int> side1 = new List<int>();
        List<int> side2 = new List<int>();

        for (int i = 0; i < triangle.Count; i++) {
            if (GetVertexRelation(plane, triangle[i]) <= 0)
                side1.Add(i);
            if (GetVertexRelation(plane, triangle[i]) >= 0)
                side2.Add(i);
        }

        // a,b,c are the indices in triangle array
        int a, b, c;

        if (side1.Count == 1) {
            a = side2[0];
            b = side2[1];
            c = side1[0];
        }
        else {
            a = side1[0];
            b = side1[1];
            c = side2[0];
        }

        return result;
    }

    Vector3 GetFaceNormal(List<int> face) {
        Vector3 edge1 = toWorldCoord(face[1]) - toWorldCoord(face[0]);
        Vector3 edge2 = toWorldCoord(face[2]) - toWorldCoord(face[1]);

        return Vector3.Cross(edge1.normalized, edge2.normalized).normalized;
    }

    FaceRelation GetFaceRelation(Plane plane, List<int> triangle) {
        float result0 = GetVertexRelation(plane, triangle[0]);
        float result1 = GetVertexRelation(plane, triangle[1]);
        float result2 = GetVertexRelation(plane, triangle[2]);

        if (result0 >= 0 && result1 >= 0 && result2 >= 0)
            return FaceRelation.Front;
        else if (result0 <= 0 && result1 <= 0 && result2 <= 0)
            return FaceRelation.Back;
        else
            return FaceRelation.Intersect;
    }

    float GetVertexRelation(Plane plane, int vertex) {
        Vector3 vert = toWorldCoord(vertex);
        return plane.a * vert.x + plane.b * vert.y + plane.c * vert.z;
    }

    public Vector3 toWorldCoord(int localPoint) { return gameObj.transform.TransformPoint(vertices[localPoint]); }
}

public class BSPNode {
    public BSPNode frontNode = null;
    public BSPNode backNode = null;

    public List<Plane> subtreeNodes = new List<Plane>();

    public Plane node;
}

public class Plane {
    public float a;
    public float b;
    public float c;
    public float d;

    public List<int> indices;
    public int triangleIndex;

    public Plane(float pA, float pB, float pC, float pD, List<int> pIndices, int pTri) {
        a = pA; b = pB; c = pC; d = pD; indices = pIndices; triangleIndex = pTri;
    }
}