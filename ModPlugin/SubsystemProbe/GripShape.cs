using UnityEngine;
namespace IKnowAGuyVR.SubsystemProbe;
// Snapshot of authored collider geometry in its rendered pose. Disabled held
// colliders remain usable without enabling physics or adding proxy colliders.
internal struct GripShape
{
 private Matrix4x4 local;private Vector3 center,half;private float radius,height;private int kind,axis;
 private Collider mesh;private Vector3 position;private Quaternion rotation;
 internal static bool TryCreate(Collider collider,out GripShape s)
 {
  s=new GripShape{local=collider.transform.worldToLocalMatrix};
  var box=collider.TryCast<BoxCollider>();if(box!=null){s.center=box.center;s.half=box.size*.5f;return true;}
  var scale=collider.transform.lossyScale;scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
  var sphere=collider.TryCast<SphereCollider>();if(sphere!=null){s.kind=1;s.local=Matrix4x4.identity;s.center=collider.transform.TransformPoint(sphere.center);s.radius=sphere.radius*Mathf.Max(scale.x,Mathf.Max(scale.y,scale.z));return true;}
  var capsule=collider.TryCast<CapsuleCollider>();if(capsule!=null){s.kind=2;s.local=Matrix4x4.TRS(collider.transform.TransformPoint(capsule.center),collider.transform.rotation,Vector3.one).inverse;s.axis=capsule.direction;s.radius=capsule.radius*Mathf.Max(scale[(s.axis+1)%3],scale[(s.axis+2)%3]);s.height=Mathf.Max(0,capsule.height*scale[s.axis]*.5f-s.radius);return true;}
  var convex=collider.TryCast<MeshCollider>();
  if(convex!=null&&convex.convex&&convex.sharedMesh!=null)
  {
   s.kind=3;s.mesh=collider;s.position=collider.transform.position;s.rotation=collider.transform.rotation;
   // Validate explicit-pose geometry access while the held collider is disabled.
   var outside=collider.transform.TransformPoint(convex.sharedMesh.bounds.max+Vector3.one);
   return (Physics.ClosestPoint(outside,collider,s.position,s.rotation)-outside).sqrMagnitude>0;
  }
  return false;
 }
 internal float Distance(Vector3 world)
 {
  if(kind==3)return Vector3.Distance(world,Physics.ClosestPoint(world,mesh,position,rotation));
  var p=local.MultiplyPoint3x4(world)-center;
  if(kind==1)return p.magnitude-radius;
  if(kind==2){p[axis]-=Mathf.Clamp(p[axis],-height,height);return p.magnitude-radius;}
  var q=new Vector3(Mathf.Abs(p.x),Mathf.Abs(p.y),Mathf.Abs(p.z))-half;
  return Vector3.Max(q,Vector3.zero).magnitude+Mathf.Min(Mathf.Max(q.x,Mathf.Max(q.y,q.z)),0);
 }
}
