/*
Credit: tsiyukino
Github: tsiyukino
Tutorial: Put/Select your avatar, Select the Clothes or anything you want to add into wardrobe, click "Add Selected Objects to Wardrobe", Click "Update Wardrobe System" to update. If you want to delete a clothes, click "Delete" next to the clothes, then click "Update...".
This is my first actually working unity script so: 
If any unexpected bugs emerge, contact me through github issue.
*/
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using UnityEditor.Animations;
using System.Linq;
using System.IO;

namespace VRLabs.AV3Manager
{
    public class WardrobeManager : EditorWindow
    {
        private VRCAvatarDescriptor avatarDescriptor;
        private List<GameObject> wardrobeItems = new List<GameObject>();
        private Vector2 scrollPosition;
        private string generatedAssetsPath = "Assets/TsiYukiData/Wardrobe/";
        private const string PARAMETER_NAME = "Wardrobe";
        private const string MENU_NAME = "Wardrobe";
        private const string SAVE_KEY_PREFIX = "TsiYuki_Wardrobe_";

        [MenuItem("TsiYuki/Wardrobe Manager")]
        public static void ShowWindow()
        {
            GetWindow<WardrobeManager>("TsiYuki's Wardrobe Manager");
        }

        private void OnEnable()
        {
            LoadWardrobeState();
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            avatarDescriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar", avatarDescriptor, typeof(VRCAvatarDescriptor), true);
            
            if (EditorGUI.EndChangeCheck() && avatarDescriptor != null)
            {
                LoadWardrobeState();
            }

            if (avatarDescriptor == null) return;

            EditorGUILayout.Space();
            if (GUILayout.Button("Add Selected Objects to Wardrobe"))
            {
                AddSelectedObjectsToWardrobe();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DisplayWardrobeItems();
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Update Wardrobe System"))
            {
                UpdateWardrobeSystem();
            }
        }

        private void SaveWardrobeState()
        {
            if (avatarDescriptor == null) return;
            
            string key = SAVE_KEY_PREFIX + avatarDescriptor.gameObject.GetInstanceID();
            string paths = string.Join(";", wardrobeItems.Select(item => 
                GetRelativePath(item.transform, avatarDescriptor.transform)));
            
            EditorPrefs.SetString(key, paths);
        }

        private void LoadWardrobeState()
        {
            wardrobeItems.Clear();
            if (avatarDescriptor == null) return;

            string key = SAVE_KEY_PREFIX + avatarDescriptor.gameObject.GetInstanceID();
            string savedPaths = EditorPrefs.GetString(key, "");
            
            if (string.IsNullOrEmpty(savedPaths)) return;

            foreach (string path in savedPaths.Split(';'))
            {
                Transform child = avatarDescriptor.transform.Find(path);
                if (child != null)
                {
                    wardrobeItems.Add(child.gameObject);
                }
            }
        }

        private void CleanupOldAssets()
        {
                foreach (var layer in avatarDescriptor.baseAnimationLayers)
                {
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && layer.animatorController != null)
                    {
                        var fxController = layer.animatorController as AnimatorController;
                        var wardrobeLayer = fxController.layers.FirstOrDefault(l => l.name == "Wardrobe");
                        if (wardrobeLayer != null)
                        {
                            var layerList = fxController.layers.ToList();
                            layerList.RemoveAll(l => l.name == "Wardrobe");
                            fxController.layers = layerList.ToArray();
                            EditorUtility.SetDirty(fxController);
                        }
                    }
                }

                RemoveParameter(avatarDescriptor, PARAMETER_NAME);

                string[] guids = AssetDatabase.FindAssets("Wardrobe_", new[] { generatedAssetsPath });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AssetDatabase.DeleteAsset(path);
                }

                var menuPath = $"{generatedAssetsPath}WardrobeMenu.asset";
                if (File.Exists(menuPath))
                {
                    AssetDatabase.DeleteAsset(menuPath);
                }
        }

        private void RemoveParameter(VRCAvatarDescriptor descriptor, string parameterName)
        {
            if (descriptor.expressionParameters != null)
            {
                var parameters = descriptor.expressionParameters.parameters.ToList();
                parameters.RemoveAll(p => p.name == parameterName);
                descriptor.expressionParameters.parameters = parameters.ToArray();
            }
        }

        private void UpdateWardrobeSystem()
        {
            if (!Directory.Exists(generatedAssetsPath))
            {
                Directory.CreateDirectory(generatedAssetsPath);
            }

            CleanupOldAssets();

            if (wardrobeItems.Count > 0)
            {
                CreateWardrobeParameter();
                CreateWardrobeAnimations();
                CreateWardrobeMenu();
                UpdateFXLayer();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void CreateWardrobeParameter()
        {
            var parameter = new VRCExpressionParameters.Parameter
            {
                name = PARAMETER_NAME,
                valueType = VRCExpressionParameters.ValueType.Int,
                defaultValue = 0,
                saved = true
            };

            AV3ManagerFunctions.AddParameter(avatarDescriptor, parameter, generatedAssetsPath);
        }

        private void CreateWardrobeAnimations()
        {
            for (int i = 0; i < wardrobeItems.Count; i++)
            {
                var clip = new AnimationClip();
                
                foreach (var item in wardrobeItems)
                {
                    var path = GetRelativePath(item.transform, avatarDescriptor.transform);
                    var curve = new AnimationCurve(new Keyframe(0, item == wardrobeItems[i] ? 1 : 0));
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
                }

                AssetDatabase.CreateAsset(clip, $"{generatedAssetsPath}Wardrobe_{i}.anim");
            }
        }

        private void UpdateFXLayer()
        {
            string controllerPath = $"{generatedAssetsPath}WardrobeFX.controller";
            AnimatorController controller = new AnimatorController();
            AssetDatabase.CreateAsset(controller, controllerPath);

            var parameter = new AnimatorControllerParameter
            {
                name = PARAMETER_NAME,
                type = AnimatorControllerParameterType.Int,
                defaultInt = 0
            };
            controller.AddParameter(parameter);

            var layer = new AnimatorControllerLayer
            {
                name = "Wardrobe",
                defaultWeight = 1,
                stateMachine = new AnimatorStateMachine()
            };
            AssetDatabase.CreateAsset(layer.stateMachine, $"{generatedAssetsPath}WardrobeStateMachine.asset");
            controller.AddLayer(layer);

            var defaultState = layer.stateMachine.AddState("Default", new Vector3(300, 100));
            
            for (int i = 0; i < wardrobeItems.Count; i++)
            {
                var state = layer.stateMachine.AddState($"Wardrobe_{i}", new Vector3(300, 100 * (i + 2)));
                state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{generatedAssetsPath}Wardrobe_{i}.anim");

                var transition = defaultState.AddTransition(state);
                transition.hasExitTime = false;
                transition.duration = 0;
                transition.conditions = new[]
                {
                    new AnimatorCondition
                    {
                        mode = AnimatorConditionMode.Equals,
                        parameter = PARAMETER_NAME,
                        threshold = i
                    }
                };

                var exitTransition = state.AddTransition(defaultState);
                exitTransition.hasExitTime = false;
                exitTransition.duration = 0;
                exitTransition.conditions = new[]
                {
                    new AnimatorCondition
                    {
                        mode = AnimatorConditionMode.NotEqual,
                        parameter = PARAMETER_NAME,
                        threshold = i
                    }
                };
            }

            AV3ManagerFunctions.MergeToLayer(avatarDescriptor, controller, VRCAvatarDescriptor.AnimLayerType.FX, generatedAssetsPath);
        }

        private void CreateWardrobeMenu()
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            
            for (int i = 0; i < wardrobeItems.Count; i++)
            {
                var control = new VRCExpressionsMenu.Control
                {
                    name = wardrobeItems[i].name,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = PARAMETER_NAME },
                    value = i
                };
                menu.controls.Add(control);
            }

            AssetDatabase.CreateAsset(menu, $"{generatedAssetsPath}WardrobeMenu.asset");
            AV3ManagerFunctions.AddSubMenu(avatarDescriptor, menu, MENU_NAME, generatedAssetsPath);
        }

        private string GetRelativePath(Transform target, Transform root)
        {
            string path = target.name;
            Transform current = target.parent;

            while (current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private void AddSelectedObjectsToWardrobe()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            bool changed = false;
            
            foreach (GameObject obj in selectedObjects)
            {
                if (!wardrobeItems.Contains(obj) && obj.transform.IsChildOf(avatarDescriptor.transform))
                {
                    wardrobeItems.Add(obj);
                    changed = true;
                }
            }

            if (changed)
            {
                SaveWardrobeState();
            }
        }

        private void DisplayWardrobeItems()
        {
            bool changed = false;
            
            for (int i = wardrobeItems.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(wardrobeItems[i], typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    wardrobeItems.RemoveAt(i);
                    changed = true;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (changed)
            {
                SaveWardrobeState();
            }
        }
    }
}