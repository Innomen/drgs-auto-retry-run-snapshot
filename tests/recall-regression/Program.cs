using System.Reflection;
using DRGSAutoRetry;

var order = new List<int>();
var dead = new UnityEngine.Object { Alive = false };
var live = new UnityEngine.Object();
var plain = new object();
var a = new Il2CppSystem.Action(dead, () => throw new NullReferenceException());
var b = new Il2CppSystem.Action(live, () => order.Add(1));
var c = new Il2CppSystem.Action(null, () => order.Add(2));
var d = new Il2CppSystem.Action(plain, () => order.Add(3));
MapGenerator.OnRecallAll = Il2CppSystem.Action.Combine(a,b,a,c,d);
Clean();
MapGenerator.OnRecallAll!.Invoke();
Check(order.SequenceEqual(new[]{1,2,3}));
var kept = MapGenerator.OnRecallAll;
Clean(); Check(ReferenceEquals(kept,MapGenerator.OnRecallAll));
MapGenerator.OnRecallAll=Il2CppSystem.Action.Combine(a,a);
Clean(); Check(MapGenerator.OnRecallAll == null);
Clean();
MapGenerator.OnRecallAll = new Il2CppSystem.Action(live, () => throw new InvalidOperationException());
Clean();
try { MapGenerator.OnRecallAll.Invoke(); throw new Exception("Swallowed live error"); }
catch (InvalidOperationException) { }
Console.WriteLine("Recall regression passed: destroyed duplicates removed; live/static/non-Unity callbacks and order preserved; null/all-dead safe; live errors propagate; second pass idempotent.");
static void Clean() => typeof(RecallCleanup).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
static void Check(bool value) { if(!value) throw new Exception("Regression failed"); }

namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch(Type t,string n):Attribute {} }
namespace Il2CppInterop.Runtime { }
namespace UnityEngine { public class Object { public bool Alive=true; public static implicit operator bool(Object value)=>value.Alive; } }
namespace Il2CppSystem {
 public class Object(object value) { public T? TryCast<T>() where T:class => value as T; }
 public class Delegate {
  internal List<Action> Calls=new();
  public Object? Target {get;set;}
  public T Cast<T>() where T:class => (this as T)!;
  public static Delegate? Remove(Delegate source,Delegate callback) {
   var remaining=source.Calls.ToList();
   int index=remaining.FindLastIndex(x=>ReferenceEquals(x,callback));
   if(index>=0) remaining.RemoveAt(index);
   return remaining.Count==0 ? null : new Action {Calls=remaining};
  }
 }
 public class Action:Delegate {
  private System.Action? body;
  public Action() {}
  public Action(object? target,System.Action callback) {Target=target==null?null:new Object(target);body=callback;Calls.Add(this);}
  public Delegate[] GetInvocationList()=>Calls.Cast<Delegate>().ToArray();
  public void Invoke() {foreach(var item in Calls) item.body!();}
  public static Action Combine(params Action[] items)=>new(){Calls=items.ToList()};
 }
}
public class MapGenerator { public static Il2CppSystem.Action? OnRecallAll; public void CleanUp(){} }
namespace DRGSAutoRetry { public class Plugin {public static Plugin Instance=new();public LogStub Log=new();} public class LogStub {public void LogInfo(string s){}} }
