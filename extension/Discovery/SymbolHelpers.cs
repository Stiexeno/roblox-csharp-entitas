using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RobloxCSharp.Extensions.Entities
{
	// Stateless utilities used by EntitiesExtension.PreSourceDiscovery —
	// Roslyn symbol introspection, file I/O for codegen output, and
	// metadata-reference assembly collection for the on-the-fly
	// CSharpCompilation we build per dev tick.
	internal static class SymbolHelpers
	{
		public static void AppendCsFiles(string dir, List<SyntaxTree> trees)
		{
			if (!Directory.Exists(dir)) return;
			foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(file);
				trees.Add(CSharpSyntaxTree.ParseText(text, path: file));
			}
		}

		public static List<MetadataReference> BuildReferences()
		{
			List<MetadataReference> refs = new();
			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (asm.IsDynamic) continue;
				string loc = asm.Location;
				if (string.IsNullOrEmpty(loc)) continue;
				if (!seen.Add(loc)) continue;
				refs.Add(MetadataReference.CreateFromFile(loc));
			}
			return refs;
		}

		public static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol iface)
		{
			foreach (INamedTypeSymbol candidate in type.AllInterfaces)
			{
				if (SymbolEqualityComparer.Default.Equals(candidate, iface)) return true;
			}
			return false;
		}

		public static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
		{
			INamedTypeSymbol cursor = type;
			while (cursor is not null)
			{
				if (SymbolEqualityComparer.Default.Equals(cursor, baseType)) return true;
				cursor = cursor.BaseType;
			}
			return false;
		}

		// User-declared subclass (GameAttribute -> ContextAttribute): the
		// subclass calls `base("Game")` in its constructor, so the
		// constructor arg flows up as the first AttributeData ctor argument
		// when the subclass declares no explicit ones — but most subclasses
		// do, so we fall back to the attribute class name minus the
		// "Attribute" suffix.
		public static string ExtractContextName(AttributeData attr)
		{
			if (attr.ConstructorArguments.Length > 0)
			{
				TypedConstant first = attr.ConstructorArguments[0];
				if (first.Value is string s && !string.IsNullOrEmpty(s)) return s;
			}
			string typeName = attr.AttributeClass?.Name;
			if (string.IsNullOrEmpty(typeName)) return null;
			if (typeName.EndsWith("Attribute", StringComparison.Ordinal))
				typeName = typeName.Substring(0, typeName.Length - "Attribute".Length);
			return typeName;
		}

		// Write-if-changed — the codegen runs every dev tick; skipping
		// unchanged-byte writes avoids spurious file-watcher churn.
		public static void WriteFile(string dir, string name, string content)
		{
			string path = Path.Combine(dir, name);
			if (File.Exists(path))
			{
				string existing = File.ReadAllText(path);
				if (existing == content) return;
			}
			File.WriteAllText(path, content);
		}
	}
}
