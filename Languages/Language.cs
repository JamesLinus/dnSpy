﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.PE;
using dnSpy.Contracts.Files;
using dnSpy.Contracts.Highlighting;
using dnSpy.Contracts.Languages;
using dnSpy.Decompiler;
using dnSpy.Languages.CSharp;
using dnSpy.Languages.Properties;
using dnSpy.NRefactory;
using ICSharpCode.Decompiler;

namespace dnSpy.Languages {
	/// <summary>
	/// Base class for language-specific decompiler implementations.
	/// </summary>
	public abstract class Language : ILanguage {
		public abstract string GenericNameUI { get; }
		public abstract string UniqueNameUI { get; }
		public abstract double OrderUI { get; }
		public abstract Guid GenericGuid { get; }
		public abstract Guid UniqueGuid { get; }

		public abstract string FileExtension { get; }

		public virtual string ProjectFileExtension {
			get { return null; }
		}

		public void WriteName(ISyntaxHighlightOutput output, TypeDef type) {
			FormatTypeName(SyntaxHighlightOutputToTextOutput.Create(output), type);
		}

		public void WriteType(ISyntaxHighlightOutput output, ITypeDefOrRef type, bool includeNamespace, ParamDef pd = null) {
			TypeToString(SyntaxHighlightOutputToTextOutput.Create(output), type, includeNamespace, pd);
		}

		public void WriteName(ISyntaxHighlightOutput output, PropertyDef property, bool? isIndexer) {
			FormatPropertyName(SyntaxHighlightOutputToTextOutput.Create(output), property, isIndexer);
		}

		public virtual void Decompile(MethodDef method, ITextOutput output, DecompilationOptions options) {
			this.WriteCommentLine(output, TypeToString(method.DeclaringType, true) + "." + method.Name);
		}

		public virtual void Decompile(PropertyDef property, ITextOutput output, DecompilationOptions options) {
			this.WriteCommentLine(output, TypeToString(property.DeclaringType, true) + "." + property.Name);
		}

		public virtual void Decompile(FieldDef field, ITextOutput output, DecompilationOptions options) {
			this.WriteCommentLine(output, TypeToString(field.DeclaringType, true) + "." + field.Name);
		}

		public virtual void Decompile(EventDef ev, ITextOutput output, DecompilationOptions options) {
			this.WriteCommentLine(output, TypeToString(ev.DeclaringType, true) + "." + ev.Name);
		}

		public virtual void Decompile(TypeDef type, ITextOutput output, DecompilationOptions options) {
			this.WriteCommentLine(output, TypeToString(type, true));
		}

		public virtual void DecompileNamespace(string @namespace, IEnumerable<TypeDef> types, ITextOutput output, DecompilationOptions options) {
			this.WriteCommentLine(output, string.IsNullOrEmpty(@namespace) ? string.Empty : IdentifierEscaper.Escape(@namespace));
			this.WriteCommentLine(output, string.Empty);
			this.WriteCommentLine(output, Languages_Resources.Decompile_Namespace_Types);
			this.WriteCommentLine(output, string.Empty);
			foreach (var type in types) {
				this.WriteCommentBegin(output, true);
				output.WriteReference(IdentifierEscaper.Escape(type.Name), type, TextTokenType.Comment);
				this.WriteCommentEnd(output, true);
				output.WriteLine();
			}
		}

		protected void WriteModuleAssembly(IDnSpyFile file, ITextOutput output, DecompilationOptions options, DecompileAssemblyFlags flags) {
			DecompileAssemblyInternal(file, output, options, flags);
			bool decompileAsm = (flags & DecompileAssemblyFlags.Assembly) != 0;
			bool decompileMod = (flags & DecompileAssemblyFlags.Module) != 0;
			output.WriteLine();
			ModuleDef mainModule = file.ModuleDef;
			if (decompileMod && mainModule.Types.Count > 0) {
				this.WriteCommentBegin(output, true);
				output.Write(Languages_Resources.Decompile_GlobalType + " ", TextTokenType.Comment);
				output.WriteReference(IdentifierEscaper.Escape(mainModule.GlobalType.FullName), mainModule.GlobalType, TextTokenType.Comment);
				output.WriteLine();
			}
			if (decompileMod || decompileAsm)
				this.PrintEntryPoint(file, output);
			if (decompileMod) {
				this.WriteCommentLine(output, Languages_Resources.Decompile_Architecture + " " + GetPlatformDisplayName(mainModule));
				if (!mainModule.IsILOnly) {
					this.WriteCommentLine(output, Languages_Resources.Decompile_ThisAssemblyContainsUnmanagedCode);
				}
				string runtimeName = GetRuntimeDisplayName(mainModule);
				if (runtimeName != null) {
					this.WriteCommentLine(output, Languages_Resources.Decompile_Runtime + " " + runtimeName);
				}
			}
			if ((decompileMod || decompileAsm) && file.PEImage != null) {
				this.WriteCommentBegin(output, true);
				uint ts = file.PEImage.ImageNTHeaders.FileHeader.TimeDateStamp;
				var date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts);
				var dateString = date.ToString(CultureInfo.CurrentUICulture.DateTimeFormat);
				output.Write(string.Format(Languages_Resources.Decompile_Timestamp, ts, dateString), TextTokenType.Comment);
				this.WriteCommentEnd(output, true);
				output.WriteLine();
			}
			if (decompileMod || decompileAsm)
				output.WriteLine();
		}

		public virtual void DecompileAssembly(IDnSpyFile file, ITextOutput output, DecompilationOptions options, DecompileAssemblyFlags flags = DecompileAssemblyFlags.AssemblyAndModule) {
			DecompileAssemblyInternal(file, output, options, flags);
		}

		void DecompileAssemblyInternal(IDnSpyFile file, ITextOutput output, DecompilationOptions options, DecompileAssemblyFlags flags = DecompileAssemblyFlags.AssemblyAndModule) {
			bool decompileAsm = (flags & DecompileAssemblyFlags.Assembly) != 0;
			bool decompileMod = (flags & DecompileAssemblyFlags.Module) != 0;
			this.WriteCommentLine(output, file.Filename);
			if (decompileAsm && file.AssemblyDef != null) {
				if (file.AssemblyDef.IsContentTypeWindowsRuntime) {
					this.WriteCommentLine(output, file.AssemblyDef.Name + " [WinRT]");
				}
				else {
					this.WriteCommentLine(output, file.AssemblyDef.FullName);
				}
			}
			else if (decompileMod) {
				this.WriteCommentLine(output, file.ModuleDef.Name);
			}
		}

		protected void PrintEntryPoint(IDnSpyFile assembly, ITextOutput output) {
			var ep = GetEntryPoint(assembly.ModuleDef);
			if (ep is uint)
				this.WriteCommentLine(output, string.Format(Languages_Resources.Decompile_NativeEntryPoint, (uint)ep));
			else if (ep is MethodDef) {
				var epMethod = (MethodDef)ep;
				WriteCommentBegin(output, true);
				output.Write(Languages_Resources.Decompile_EntryPoint + " ", TextTokenType.Comment);
				if (epMethod.DeclaringType != null) {
					output.WriteReference(IdentifierEscaper.Escape(epMethod.DeclaringType.FullName), epMethod.DeclaringType, TextTokenType.Comment);
					output.Write(".", TextTokenType.Comment);
				}
				output.WriteReference(IdentifierEscaper.Escape(epMethod.Name), epMethod, TextTokenType.Comment);
				WriteCommentEnd(output, true);
				output.WriteLine();
			}
		}

		object GetEntryPoint(ModuleDef module) {
			int maxIters = 1;
			for (int i = 0; module != null && i < maxIters; i++) {
				var rva = module.NativeEntryPoint;
				if (rva != 0)
					return (uint)rva;

				var manEp = module.ManagedEntryPoint;
				var ep = manEp as MethodDef;
				if (ep != null)
					return ep;

				var file = manEp as FileDef;
				if (file == null)
					return null;

				var asm = module.Assembly;
				if (asm == null)
					return null;
				maxIters = asm.Modules.Count;

				module = asm.Modules.FirstOrDefault(m => File.Exists(m.Location) && StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(m.Location), file.Name));
			}

			return null;
		}

		protected void WriteCommentLineDeclaringType(ITextOutput output, IMemberDef member) {
			WriteCommentBegin(output, true);
			output.WriteReference(TypeToString(member.DeclaringType, includeNamespace: true), member.DeclaringType, TextTokenType.Comment);
			WriteCommentEnd(output, true);
			output.WriteLine();
		}

		public virtual void WriteCommentBegin(ITextOutput output, bool addSpace) {
			if (addSpace)
				output.Write("// ", TextTokenType.Comment);
			else
				output.Write("//", TextTokenType.Comment);
		}

		public virtual void WriteCommentEnd(ITextOutput output, bool addSpace) {
		}

		string TypeToString(ITypeDefOrRef type, bool includeNamespace, IHasCustomAttribute typeAttributes = null) {
			var writer = new StringWriter();
			var output = new PlainTextOutput(writer);
			TypeToString(output, type, includeNamespace, typeAttributes);
			return writer.ToString();
		}

		protected virtual void TypeToString(ITextOutput output, ITypeDefOrRef type, bool includeNamespace, IHasCustomAttribute typeAttributes = null) {
			if (type == null)
				return;
			if (includeNamespace)
				output.Write(IdentifierEscaper.Escape(type.FullName), TextTokenHelper.GetTextTokenType(type));
			else
				output.Write(IdentifierEscaper.Escape(type.Name), TextTokenHelper.GetTextTokenType(type));
		}

		public virtual void WriteToolTip(ISyntaxHighlightOutput output, IMemberRef member, IHasCustomAttribute typeAttributes) {
			new SimpleCSharpPrinter(output, SimplePrinterFlags.Default).WriteToolTip(member);
		}

		public virtual void WriteToolTip(ISyntaxHighlightOutput output, IVariable variable, string name) {
			new SimpleCSharpPrinter(output, SimplePrinterFlags.Default).WriteToolTip(variable, name);
		}

		public virtual void Write(ISyntaxHighlightOutput output, IMemberRef member, SimplePrinterFlags flags) {
			new SimpleCSharpPrinter(output, flags).Write(member);
		}

		protected static string GetName(IVariable variable, string name) {
			if (!string.IsNullOrWhiteSpace(name))
				return name;
			var n = variable.Name;
			if (!string.IsNullOrWhiteSpace(n))
				return n;
			return string.Format("#{0}", variable.Index);
		}

		protected virtual void FormatPropertyName(ITextOutput output, PropertyDef property, bool? isIndexer = null) {
			if (property == null)
				throw new ArgumentNullException("property");
			output.Write(IdentifierEscaper.Escape(property.Name), TextTokenHelper.GetTextTokenType(property));
		}

		protected virtual void FormatTypeName(ITextOutput output, TypeDef type) {
			if (type == null)
				throw new ArgumentNullException("type");
			output.Write(IdentifierEscaper.Escape(type.Name), TextTokenHelper.GetTextTokenType(type));
		}

		public virtual bool ShowMember(IMemberRef member, DecompilerSettings decompilerSettings) {
			return true;
		}

		protected static string GetPlatformDisplayName(ModuleDef module) {
			switch (module.Machine) {
			case Machine.I386:
				if (module.Is32BitPreferred)
					return Languages_Resources.Decompile_AnyCPU32BitPreferred;
				else if (module.Is32BitRequired)
					return "x86";
				else
					return Languages_Resources.Decompile_AnyCPU64BitPreferred;
			case Machine.AMD64:
				return "x64";
			case Machine.IA64:
				return "Itanium";
			case Machine.ARMNT:
				return "ARM";
			case Machine.ARM64:
				return "ARM64";
			default:
				return module.Machine.ToString();
			}
		}

		protected static string GetRuntimeDisplayName(ModuleDef module) {
			if (module.IsClr10)
				return ".NET 1.0";
			if (module.IsClr11)
				return ".NET 1.1";
			if (module.IsClr20)
				return ".NET 2.0";
			if (module.IsClr40)
				return ".NET 4.0";
			return null;
		}

		public virtual bool CanDecompile(DecompilationType decompilationType) {
			return false;
		}

		public virtual void Decompile(DecompilationType decompilationType, object data) {
			throw new NotImplementedException();
		}
	}
}
