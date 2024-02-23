#if DEBUG_CUSTOM_STATIC_BATCHING
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Text;

public class PrintMeshLayout
{
	[MenuItem("Tools/Print Mesh Attributes")]
	public static void PrintLayout()
	{
	
	
		GameObject sel = Selection.activeGameObject;
		Renderer r = sel.GetComponent<Renderer>();
		Mesh m = null;
		if (r == null) return;
		if (r.GetType() == typeof(MeshRenderer))
		{
			m = r.GetComponent<MeshFilter>().sharedMesh;
		}
		else if (r.GetType() == typeof(SkinnedMeshRenderer))
		{
			m = ((SkinnedMeshRenderer)r).sharedMesh;
		}
		if (m == null) return;
		VertexAttributeDescriptor[] attr = m.GetVertexAttributes();

		StringBuilder sb = new StringBuilder();
		sb.AppendLine("Vertex Layout:\n");
		for (int i = 0; i < attr.Length; i++)
		{
			switch (attr[i].attribute)
			{
				case VertexAttribute.Position:
					sb.Append("Position");
					break;
				case VertexAttribute.Normal:
					sb.Append("Normal");
					break;
				case VertexAttribute.Tangent:
					sb.Append("Tangent");
					break;
				case VertexAttribute.Color:
					sb.Append("Color");
					break;
				case VertexAttribute.TexCoord0:
					sb.Append("TexCoord0");
					break;
				case VertexAttribute.TexCoord1:
					sb.Append("TexCoord1");
					break;
				case VertexAttribute.TexCoord2:
					sb.Append("TexCoord2");
					break;
				case VertexAttribute.TexCoord3:
					sb.Append("TexCoord3");
					break;
				case VertexAttribute.TexCoord4:
					sb.Append("TexCoord4");
					break;
				case VertexAttribute.TexCoord5:
					sb.Append("TexCoord5");
					break;
				case VertexAttribute.TexCoord6:
					sb.Append("TexCoord6");
					break;
				case VertexAttribute.TexCoord7:
					sb.Append("TexCoord7");
					break;
				case VertexAttribute.BlendIndices:
					sb.Append("BlendIndices");
					break;
				case VertexAttribute.BlendWeight:
					sb.Append("BlendWeight");
					break;
			}
			sb.Append(string.Format(": Stream:{0}, Format: {1}, Dimension: {2}\n", attr[i].stream, VertexAttributeFormat.GetName(typeof(VertexAttributeFormat), attr[i].format), attr[i].dimension));
		}
		Debug.Log(sb.ToString());
	}
}
#endif