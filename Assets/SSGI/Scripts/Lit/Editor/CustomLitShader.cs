using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CustomLitGUI{
    partial class CustomLitShader : BaseShaderGUI{
        //自定义效果-单行显示图片
        internal class SingleLineDrawer : MaterialPropertyDrawer{
            public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor) {
                editor.TexturePropertySingleLine(label, prop);
            }

            public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor) {
                return 0;
            }
        }

        //自定义效果-折行显示图片
        internal class FoldoutDrawer : MaterialPropertyDrawer{
            bool showPosition;

            public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor) {
                showPosition = EditorGUILayout.Foldout(showPosition, label);
                prop.floatValue = Convert.ToSingle(showPosition);
            }

            public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor) {
                return 0;
            }
        }

        static Dictionary<string, MaterialProperty> s_MaterialProperty = new Dictionary<string, MaterialProperty>();
        static List<MaterialData> s_List = new List<MaterialData>();

        public override void OnGUI(MaterialEditor materialEditorIn, MaterialProperty[] properties) {
            base.OnGUI(materialEditorIn, properties);


            EditorGUILayout.Space();

            Shader shader = (materialEditor.target as Material).shader;
            s_List.Clear();
            s_MaterialProperty.Clear();
            for (int i = 0; i < properties.Length; i++) {
                var propertie = properties[i];
                var attributes = shader.GetPropertyAttributes(i);
                foreach (var item in attributes) {
                    if (item.Contains("ext")) {
                        if (!s_MaterialProperty.ContainsKey(propertie.name)) {
                            s_MaterialProperty[propertie.name] = propertie;
                            s_List.Add(new MaterialData() { prop = propertie, indentLevel = false });
                        }
                    }
                    else if (item.Contains("Toggle")) {
                        //根据Toggle标签每帧启动宏
                        if (s_MaterialProperty.TryGetValue(propertie.name, out var __)) {
                            if (propertie.type == MaterialProperty.PropType.Float) {
                                string keyword = "";
                                Match match = Regex.Match(item, @"(\w+)\s*\((.*)\)");
                                if (match.Success)
                                    keyword = match.Groups[2].Value.Trim();
                                if (string.IsNullOrEmpty(keyword))
                                    keyword = propertie.name.ToUpperInvariant() + "_ON";
                                foreach (Material material in propertie.targets) {
                                    if (propertie.floatValue == 1.0f)
                                        material.EnableKeyword(keyword);
                                    else
                                        material.DisableKeyword(keyword);
                                }
                            }
                        }
                    }
                    else if (item.StartsWith("if")) {
                        Match match = Regex.Match(item, @"(\w+)\s*\((.*)\)");
                        if (match.Success) {
                            var name = match.Groups[2].Value.Trim();
                            if (s_MaterialProperty.TryGetValue(name, out var a)) {
                                if (a.floatValue == 0f) {
                                    //如果有if标签，并且Foldout没有展开不进行绘制
                                    s_List.RemoveAt(s_List.Count - 1);
                                }
                                else
                                    s_List[s_List.Count - 1].indentLevel = true;
                            }
                        }
                    }
                }
            }

            PropertiesDefaultGUI(materialEditor, s_List);
        }

        public class MaterialData{
            public MaterialProperty prop;
            public bool indentLevel = false;
        }

        public void PropertiesDefaultGUI(MaterialEditor materialEditor, List<MaterialData> props) {
            for (int i = 0; i < props.Count; i++) {
                MaterialProperty prop = props[i].prop;
                bool indentLevel = props[i].indentLevel;
                if ((prop.flags & (MaterialProperty.PropFlags.HideInInspector | MaterialProperty.PropFlags.PerRendererData)) == MaterialProperty.PropFlags.None) {
                    float propertyHeight = materialEditor.GetPropertyHeight(prop, prop.displayName);
                    Rect controlRect = EditorGUILayout.GetControlRect(true, propertyHeight, EditorStyles.layerMaskField);
                    if (indentLevel) EditorGUI.indentLevel++;
                    materialEditor.ShaderProperty(controlRect, prop, prop.displayName);
                    if (indentLevel) EditorGUI.indentLevel--;
                }
            }
        }
    }
}