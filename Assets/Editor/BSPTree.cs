using UnityEngine;
using System.Collections;
using System.Collections.Generic;


/// <summary>
/// All vertices convert to world coordinate for intersection testing
/// </summary>
public class BSPTree {

    class BSPNode {
        public BSPNode frontSubtree = null;
        public BSPNode backSubtree = null;
        public Plane node;
    }
 
    class Plane {
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

    enum FaceRelation {
        Front,
        Back,
        Intersect
    }

    List<Plane> planeEquations;
    BSPNode root;
    Mesh mesh;
    GameObject gameObj;
    List<int> triangles;
    List<Vector3> vertices;

    public BSPTree(GameObject pGameObj, Mesh pMesh) {
        gameObj = pGameObj;
        mesh = pMesh;

        triangles = new List<int>(mesh.triangles);
        vertices = new List<Vector3>(mesh.vertices);
        planeEquations = new List<Plane>();

        CalcPlaneEquations();
    }


    void CalcPlaneEquations() {
        for (int i = 0; i < triangles.Count ; i += 3) {
            Vector3 normal = GetFaceNormal(new List<int> { triangles[i], triangles[i + 1], triangles[i + 2] });
            Plane p = new Plane(normal.x, normal.y, normal.z, -Vector3.Dot(normal, gameObj.transform.TransformPoint(vertices[triangles[i]])),
                                new List<int> { triangles[i], triangles[i + 1], triangles[i + 2] }, i/3);
            planeEquations.Add(p);
        }
    }

    Vector3 GetFaceNormal(List<int> face) {
        Vector3 edge1 = toWorldCoord(face[1]) - toWorldCoord(face[0]);
        Vector3 edge2 = toWorldCoord(face[2]) - toWorldCoord(face[1]);

        return Vector3.Cross(edge1.normalized, edge2.normalized).normalized;
    }

    FaceRelation GetFaceRelation(Plane plane, List<int> triangle) {

        return FaceRelation.Front;
    }

    Vector3 toWorldCoord(int localPoint) { return gameObj.transform.TransformPoint(vertices[localPoint]); }
}
