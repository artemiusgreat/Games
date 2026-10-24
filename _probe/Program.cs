using System;
using System.Linq;
using System.Reflection;
var _ = typeof(Stride.Engine.Entity);
var asms = AppDomain.CurrentDomain.GetAssemblies().Where(a => (a.GetName().Name ?? "").StartsWith("Stride")).ToList();
Type T(string n) => asms.Select(a => a.GetType(n)).First(x => x != null);
var gp = T("Stride.Graphics.GeometricPrimitives.GeometricPrimitive");
Console.WriteLine("GeometricPrimitive instance props: " + string.Join(", ", gp.GetProperties(BindingFlags.Public|BindingFlags.Instance).Select(p => p.PropertyType.Name + " " + p.Name)));
foreach (var nt in new[]{"Cube","Sphere","Capsule","Cylinder","Cone"})
    foreach (var m in gp.GetNestedType(nt).GetMethods().Where(m => m.Name == "New"))
        Console.WriteLine($"{nt}.New({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
var bv = T("Stride.Graphics.Buffer").GetNestedType("Vertex");
foreach (var m in bv.GetMethods().Where(m => m.Name.StartsWith("New")).Take(6))
    Console.WriteLine($"Buffer.Vertex.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) -> {m.ReturnType.Name}");
var bi = T("Stride.Graphics.Buffer").GetNestedType("Index");
foreach (var m in bi.GetMethods().Where(m => m.Name.StartsWith("New")).Take(4))
    Console.WriteLine($"Buffer.Index.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) -> {m.ReturnType.Name}");
var vpnc = T("Stride.Graphics.VertexPositionNormalColor");
Console.WriteLine("VPNC fields: " + string.Join(", ", vpnc.GetFields().Select(f => f.FieldType.Name + " " + f.Name)) + " | Layout prop: " + (vpnc.GetProperty("Layout") != null));
var mat = T("Stride.Rendering.Material");
foreach (var m in mat.GetMethods(BindingFlags.Public|BindingFlags.Static).Where(m=>m.Name=="New"))
    Console.WriteLine($"Material.New({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
var vbb = T("Stride.Graphics.VertexBufferBinding");
Console.WriteLine("VBB ctors: " + string.Join(" | ", vbb.GetConstructors().Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)) + ")")));
var ibb = T("Stride.Graphics.IndexBufferBinding");
Console.WriteLine("IBB ctors: " + string.Join(" | ", ibb.GetConstructors().Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)) + ")")));
var sc = T("Stride.Engine.ScriptComponent");
Console.WriteLine("ScriptComponent.Game type: " + sc.GetProperty("Game").PropertyType.FullName);
var igame = asms.SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.Name == "IGame");
Console.WriteLine("IGame methods: " + (igame == null ? "?" : string.Join(", ", igame.GetMethods().Select(m => m.Name).Distinct())));
var ent = T("Stride.Engine.Entity");
Console.WriteLine("Entity props (Scene/Add): " + string.Join(", ", ent.GetProperties().Where(p => p.Name.Contains("Scene")).Select(p => p.Name)) + " | Add methods: " + string.Join(", ", ent.GetMethods().Where(m => m.Name == "Add").Select(m => m.GetParameters().FirstOrDefault()?.ParameterType.Name)));
var scene = T("Stride.Engine.Scene");
Console.WriteLine("Scene props: " + string.Join(", ", scene.GetProperties().Select(p => p.Name).Take(10)));
