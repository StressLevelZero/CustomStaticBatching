#if DEBUG_CUSTOM_STATIC_BATCHING
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SLZ.CustomStaticBatching
{
	public class TestLocalKWs
	{
		[MenuItem("Tools/Print Shader Keyword Idxs")]
		public static void Test()
		{
			Shader selection = (Shader)Selection.activeObject;
			if (selection != null )
			{
				UnityEngine.Rendering.LocalKeyword[] kws = selection.keywordSpace.keywords;
				string message = "Local Keywords: \n";
				for ( int i = 0; i < kws.Length; i++ )
				{
					message += string.Format("{0} : {1}\n", ReflectKWFields.GetIndex(kws[i]), kws[i].name);
				}
				Debug.Log(message);
			}
		}
	}
}
#endif