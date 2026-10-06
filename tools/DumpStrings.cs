using System;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
class DumpStrings {
    static void DumpIL(TypeDefinition t, StreamWriter w) {
        foreach (var m in t.Methods) if (m.HasBody) {
            w.WriteLine("METHOD " + m.FullName);
            foreach (var i in m.Body.Instructions) w.WriteLine(i.ToString());
        }
        foreach (var n in t.NestedTypes) DumpIL(n, w);
    }
    static void Dump(TypeDefinition t, StreamWriter w) {
        foreach (var m in t.Methods) if (m.HasBody) {
            foreach (var i in m.Body.Instructions)
                if (i.OpCode == OpCodes.Ldstr)
                    w.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes((string)i.Operand)));
        }
        foreach (var n in t.NestedTypes) Dump(n, w);
    }
    static void Main(string[] args) {
        using (var a = AssemblyDefinition.ReadAssembly(args[0]))
        using (var w = new StreamWriter(args[1], false, Encoding.UTF8))
            foreach (var t in a.MainModule.Types) Dump(t, w);
        if (args.Length > 2)
            using (var a = AssemblyDefinition.ReadAssembly(args[0]))
            using (var w = new StreamWriter(args[2], false, Encoding.UTF8))
                foreach (var t in a.MainModule.Types) DumpIL(t, w);
    }
}
