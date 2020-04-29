using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace UnityEditorTweaks.HierarchyColoring {
    public sealed partial class HierarchyColoringWindow {
        /// <summary>
        /// Flag the type of text that is being processed by this manager
        /// </summary>
        private enum ETextType { Tag, Layer, Name, Type }

        /// <summary>
        /// Store information involved with the coloring of hierarchy elements
        /// </summary>
        private sealed class ColoringValues {
            /// <summary>
            /// Indicates what the text field relates to
            /// </summary>
            public ETextType textType = ETextType.Tag;

            /// <summary>
            /// The text that will be checked when processing objects
            /// </summary>
            public string text = "Untagged";

            /// <summary>
            /// The color that will be displayed when this text has been matched
            /// </summary>
            public Color color = Color.white;

            /// <summary>
            /// Flags if the text color will be overridden for display in the hierarchy
            /// </summary>
            public bool overrideTextColor = false;

            /// <summary>
            /// The color that will be used to display the hierarchy label if overridden
            /// </summary>
            public Color textColor = Color.black;
        }

        /// <summary>
        /// Manage the applied settings to allow for the coloring of elements within the Inspector Hierarchy Panel
        /// </summary>
        /// <remarks>
        /// This code is built on the foundations laid by:
        /// Vhalenn - https://github.com/Vhalenn/publicFiles/blob/master/customHierarchy.cs
        /// Feddas  - https://github.com/Vhalenn/publicFiles/pull/1/commits/011d346eab57f2d3ad549deeb946036e32976dfd
        /// </remarks>
        [InitializeOnLoad] private static class HierarchyColoringProcessor {
            /*----------Variables----------*/
            //CONSTANT

            /// <summary>
            /// Store the dimensions that will be used to create the display the gradient textures
            /// </summary>
            private const int DISP_TEX_WIDTH = 100,
                              DISP_TEX_HEIGHT = 25;

            /// <summary>
            /// Define the additional padding that will be applied to the backing colored rect
            /// </summary>
            private static readonly Vector2 BACK_RECT_PADDING =
#if UNITY_2018_2_OR_NEWER
                new Vector2(50f, 0f);
#else
                Vector2.zero;
#endif

            /// <summary>
            /// Store the editor preference key strings for storing processing values
            /// </summary>
            private const string PREF_PROCESS_COUNT     = "{5C51E63B-10DC-486C-9415-792AB85D139C}",
                                 PREF_PROCESS_TYPE      = "{302ED2D5-3D8E-4F1A-B5FE-792098CEA855}",
                                 PREF_PROCESS_TEXT      = "{1B21C261-AC0C-4DAA-ACCD-43DB526C8E44}",
                                 PREF_PROCESS_COLOR     = "{B3D6E894-627E-4D24-9505-D56FBAF93CB2}",
                                 PREF_PROCESS_OVER      = "{8B82D8F3-58C6-4C53-A376-A2BC9D018E73}",
                                 PREF_PROCESS_TEXTCOL   = "{2E6E7BC5-D4AC-4C03-BF8A-8811AEB3A62D}",
                                 PREF_ALLOW_MULTI       = "{B42DFB46-9317-41C6-BB53-4C87E26576ED}",
                                 PREF_USE_GRADIENT      = "{3BEDD28A-DC20-4391-88F7-A13D15ECA9DB}",
                                 PREF_LBL_INDENT        = "{4C19EBB7-89CB-417C-998C-255A4BB23F1B}";
            
            //PRIVATE

            /// <summary>
            /// Store a buffer of color values that can be applied to the displayed elements
            /// </summary>
            private static List<Color> colorBuffer;

            /// <summary>
            /// Store the color that will be used to display the text element
            /// </summary>
            private static Color? textColor;

            /// <summary>
            /// Store a cache of images that will be displayed on the required elements within the inspector
            /// </summary>
            private static Dictionary<int, Texture2D> textureCache;

            //PUBLIC

            /// <summary>
            /// Stores a collection of objects describing how the hierarchy objects should be processed
            /// </summary>
            public static List<ColoringValues> toProcess;

            /// <summary>
            /// Flag if multiple colors are allowed to be displayed for each matching entry
            /// </summary>
            public static bool allowMultiColored;

            /// <summary>
            /// Flag if a gradient should be used for the colored inspector elements
            /// </summary>
            public static bool useGradient;

            /// <summary>
            /// The pixel indentation that will be applied to the inspector elements that have color values modified
            /// </summary>
            public static Vector2 labelIndentation;

            /*----------Functions----------*/
            //PRIVATE

            /// <summary>
            /// Load the initial processing values from preferences
            /// </summary>
            static HierarchyColoringProcessor() {
                //Setup the processing array from the stored values
                int COUNT = EditorPrefs.GetInt(PREF_PROCESS_COUNT, 0);
                toProcess = new List<ColoringValues>(COUNT);
                for (int i = 0; i < COUNT; ++i) {
                    toProcess.Add(new ColoringValues {
                        textType = (ETextType)EditorPrefs.GetInt(PREF_PROCESS_TYPE + i),
                        text = EditorPrefs.GetString(PREF_PROCESS_TEXT + i),
                        overrideTextColor = EditorPrefs.GetBool(PREF_PROCESS_OVER + i)
                    });
                    if (!ColorUtility.TryParseHtmlString(EditorPrefs.GetString(PREF_PROCESS_COLOR + i), out toProcess[i].color)) {
                        Debug.LogError("Hierarchy Coloring failed to process the color for value at index " + i);
                        toProcess[i].color = Color.magenta;
                    }
                    if (!ColorUtility.TryParseHtmlString(EditorPrefs.GetString(PREF_PROCESS_TEXTCOL + i), out toProcess[i].textColor)) {
                        Debug.LogError("Hierarchy Coloring failed to process the text color for value at index " + i);
                        toProcess[i].textColor = Color.black;
                    }
                }

                //Get the flags
                allowMultiColored = EditorPrefs.GetBool(PREF_ALLOW_MULTI, true);
                useGradient = EditorPrefs.GetBool(PREF_USE_GRADIENT, true);

                //Get the indentation amount
                labelIndentation = new Vector2(
                    EditorPrefs.GetFloat(PREF_LBL_INDENT, 0f),
                    0f
                );

                //Construct the buffer for storing color information
                colorBuffer = new List<Color>();

                //Create the cache of display textures
                textureCache = new Dictionary<int, Texture2D>();

                //Setup the callback for displaying values
                EditorApplication.hierarchyWindowItemOnGUI += ProcessHierarchyElementCallback;
            }

            /// <summary>
            /// Process the supplied instance within the hierarchy to determine required coloring
            /// </summary>
            /// <param name="instanceID">The ID of the object being displayed within the Hierarchy window</param>
            /// <param name="selectionRect">The rect area to be filled with the color values</param>
            private static void ProcessHierarchyElementCallback(int instanceID, Rect selectionRect) {
                //Check there are elements to process
                if (toProcess.Count == 0) return;

                //Retrieve the Game Object that is being displayed
                GameObject display = (GameObject)EditorUtility.InstanceIDToObject(instanceID);

                //If there is a Game Object, process them
                if (display) {
                    //Clear the buffers for this object
                    colorBuffer.Clear();
                    textColor = null;

                    //Store the hash code used to access the cached display texture 
                    int hash = 17;

                    //Check to see which colors should be displayed for the object
                    for (int i = 0; i < toProcess.Count; ++i) {
                        //Check what is to be checked for this entry
                        bool include = false;
                        switch (toProcess[i].textType) {
                            //Basic string compare elements
                            case ETextType.Tag:   include = toProcess[i].text == display.tag;  break;
                            case ETextType.Layer: include = int.Parse(toProcess[i].text) == display.layer; break;
                            case ETextType.Name:  include = toProcess[i].text == display.name; break;

                            //Process the stored text as a description of a Type
                            case ETextType.Type:
                                //Check that there is text to process
                                if (!string.IsNullOrEmpty(toProcess[i].text)) {
                                    //Try to get the type that is described
                                    Type type = Type.GetType(toProcess[i].text, false);

                                    //This element is included if there is a component of the type on the object
                                    include = (type != null && display.GetComponent(type) != null);
                                }
                                break;
                        }

                        //Check of this color should be included in the display
                        if (include) {
                            //Add the color to the buffer
                            colorBuffer.Add(toProcess[i].color);

                            //Modify the hash code to cache texture elements
                            hash = hash * 31 + toProcess[i].color.GetHashCode();

                            //Check to see if the text is overridden
                            if (!textColor.HasValue && toProcess[i].overrideTextColor)
                                textColor = toProcess[i].textColor;

                            //Check to see if more then color can be used
                            if (!allowMultiColored) break;
                        }
                    }

                    //If there are colors to be displayed, do it
                    if (colorBuffer.Count > 0) {
                        //Ensure that there is a texture for the specified hash code
                        if (!textureCache.ContainsKey(hash))
                            textureCache[hash] = CreateColorTexture(colorBuffer);

                        //Display the gradient color texture 
                        EditorGUI.DrawPreviewTexture(new Rect(selectionRect.position, selectionRect.size + BACK_RECT_PADDING), textureCache[hash]);

                        //Display the label for this object
                        EditorGUI.LabelField(new Rect(selectionRect.position + labelIndentation, selectionRect.size - labelIndentation), EditorGUIUtility.ObjectContent(display, display.GetType()), new GUIStyle {
                            normal = new GUIStyleState { textColor = (textColor.HasValue ? textColor.Value : InvertColor(colorBuffer[0])) * (display.activeInHierarchy ? 1f : .5f) },
                            fontStyle = FontStyle.Bold
                        });
                    }
                }
            }

            /// <summary>
            /// Create a display texture for the given color display values
            /// </summary>
            /// <param name="colorBuffer">A buffer of the color values that are to be contained in the texture</param>
            /// <returns>Returns a Texture2D object that can be displayed</returns>
            private static Texture2D CreateColorTexture(List<Color> colorBuffer) {
                //Create a new texture to hold the values
                Texture2D tex = new Texture2D(DISP_TEX_WIDTH, DISP_TEX_HEIGHT, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave; tex.wrapMode = TextureWrapMode.Clamp;

                //Construct the array of pixels to be filled
                Color[] p = new Color[DISP_TEX_WIDTH * DISP_TEX_HEIGHT];

                //If there is only one color to set, simple set
                if (colorBuffer.Count == 1) {
                    Color toSet = colorBuffer[0];
                    for (int i = p.Length - 1; i >= 0; --i)
                        p[i] = toSet;
                }

                //Otherwise, calculate the transition that is needed to display
                else {
                    //Get the number of pixels that are to be displayed for each color segment
                    int CHUNK = Mathf.CeilToInt(DISP_TEX_WIDTH / (float)(colorBuffer.Count - (useGradient ? 1 : 0)));

                    //Process each column individually
                    for (int x = 0; x < DISP_TEX_WIDTH; ++x) {
                        //Find the color indices to use for the lerp
                        int lower = Mathf.FloorToInt(x / (float)CHUNK),
                            upper = Mathf.CeilToInt(x / (float)CHUNK);

                        //Find the color that has to be set
                        Color c = (useGradient ? 
                            Color.Lerp(colorBuffer[lower], colorBuffer[upper], (x % CHUNK) / (float)CHUNK) : 
                            colorBuffer[lower]
                        );

                        //Set all of the pixels for this height strip
                        for (int y = 0; y < DISP_TEX_HEIGHT; ++y)
                            p[y * DISP_TEX_WIDTH + x] = c;
                    }
                }

                //Set the pixel information
                tex.SetPixels(p); tex.Apply();
                return tex;
            }

            /// <summary>
            /// Retrieve the opposite color to the supplied
            /// </summary>
            /// <param name="toInvert">The color that is to be inverted</param>
            /// <returns>Returns the inverted color of the supplied</returns>
            private static Color InvertColor(Color toInvert) {
                float h, s, v;
                Color.RGBToHSV(toInvert, out h, out s, out v);
                h = (h + .5f) % 1;
                v = (v + .5f) % 1;
                return Color.HSVToRGB(h, s, v);
            }

            //PUBLIC

            /// <summary>
            /// Save all of the coloring preferences to the Editor Preferences
            /// </summary>
            public static void SaveColoringPreferences() {
                EditorPrefs.SetInt(PREF_PROCESS_COUNT, toProcess.Count);
                for (int i = 0; i < toProcess.Count; ++i) {
                    EditorPrefs.SetInt(PREF_PROCESS_TYPE + i, (int)toProcess[i].textType);
                    EditorPrefs.SetString(PREF_PROCESS_TEXT + i, toProcess[i].text);
                    EditorPrefs.SetString(PREF_PROCESS_COLOR + i, "#" + ColorUtility.ToHtmlStringRGBA(toProcess[i].color));
                    EditorPrefs.SetBool(PREF_PROCESS_OVER + i, toProcess[i].overrideTextColor);
                    EditorPrefs.SetString(PREF_PROCESS_TEXTCOL + i, "#" + ColorUtility.ToHtmlStringRGBA(toProcess[i].textColor));
                }
                EditorPrefs.SetBool(PREF_ALLOW_MULTI, allowMultiColored);
                EditorPrefs.SetBool(PREF_USE_GRADIENT, useGradient);
                EditorPrefs.SetFloat(PREF_LBL_INDENT, labelIndentation.x);
            }

            /// <summary>
            /// Clear the cached texture images so that they will be regenerated
            /// </summary>
            public static void ClearDisplayCache() { textureCache.Clear(); }
        }
    }
}