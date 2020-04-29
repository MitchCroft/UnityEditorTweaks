using System;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace UnityEditorTweaks.HierarchyColoring {
    /// <summary>
    /// Manage the current settings to allow for the dynamic coloring of hierarchy inspector elements
    /// </summary>
    public sealed partial class HierarchyColoringWindow : EditorWindow {
        /*----------Types----------*/
        //PRIVATE

        /// <summary>
        /// Store basic values that can be used to sort the displaying of type objects within the menu
        /// </summary>
        private struct SortType { public string displayName; public Type type; }

        /*----------Variables----------*/
        //SHARED

        /// <summary>
        /// Store an array of all of the assembly and namespace elements that should be ignored for the dropdown selection process
        /// </summary>
        private static readonly string[] IGNORE_ASSEMBLIES = {
            "mscorlib",
            "UnityEditor",
            "Unity.Locator",
            "System.Core",
            "Assembly-CSharp-Editor",
            "Unity.PackageManager",
            "UnityEditor.Advertisements",
            "UnityEngine.Networking",
            "UnityEditor.TestRunner",
            "nunit.framework",
            "UnityEditor.TreeEditor",
            "UnityEngine.Analytics",
            "UnityEditor.Purchasing",
            "UnityEditor.VR",
            "UnityEditor.Graphs",
            "UnityEditor.WindowsStandalone.Extensions",
            "SyntaxTree.VisualStudio.Unity.Bridge",
            "System",
            "Mono.Security",
            "System.Configuration",
            "System.Xml",
            "Mono.Cecil",
            "Unity.DataContract",
            "UnityScript",
            "Unity.Legacy.NRefactory",
            "System.Xml.Linq",
            "ExCSS.Unity",
            "Unity.IvyParser",
            "UnityEditor.iOS.Extensions.Xcode",
            "SyntaxTree.VisualStudio.Unity.Messaging",
            "Boo.Lang.Compiler",
            "Boo.Lang",
            "Boo.Lang.Parser",
            "Microsoft.GeneratedCode",
            "Unity.SerializationLogic",
        };

        /// <summary>
        /// Store an array of assemblies that can be searched for Type values when manually entering a type string
        /// </summary>
        private static readonly Assembly[] SEARCH_ASSEMBLIES;

        /// <summary>
        /// Store a pre-established generic menu that will be displayed to allow for selection of Type objects from a menu
        /// </summary>
        private static readonly GenericMenu TYPE_SELECTION_MENU;

        /// <summary>
        /// Callback that will be raised when a type option has been selected on the TYPE_SELECTION_MENU
        /// </summary>
        private static Action<Type> onTypeSelected;

        //CONST

        /// <summary>
        /// Store the line buffer space that will be used when displaying the property values
        /// </summary>
        private const float LINE_BUFFER_SPACE = 2f;

        //VISIBLE

        /// <summary>
        /// Store a displayable list of elements that will be processed by this manager
        /// </summary>
        private ReorderableList processElementsList;

        /// <summary>
        /// The scroll progress that is used when displaying the re-orderable list of process elements
        /// </summary>
        private Vector2 processScrollProgress;

        /// <summary>
        /// Store the GUI content labels that will be displayed within this window
        /// </summary>
        private GUIContent displayHeader,
                           multiLabel,
                           gradientLabel,
                           indentLabel,
                           processingHeader,
                           colorLabel,
                           overrideLabel,
                           textColLabel;

        /*----------Functions----------*/
        //PRIVATE

        /// <summary>
        /// Initialise the contained elements for the displaying of the options to the user
        /// </summary>
        static HierarchyColoringWindow() {
            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////--------------------------Identify Assemblies to Search-------------------------//////////
            ////////////////////////////////////////////////////////////////////////////////////////////////////

            //Identify all of the types that are to be displayed
            List<SortType> identified = new List<SortType>();

            //Usable types must inherit component or be an interface, store the Component type for checking
            Type componentType = typeof(Component);

            //Process all of the assemblies loaded within the project
            List<Assembly> toCheck = new List<Assembly>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                //Make sure this assembly isn't being ignored
                bool found = false;
                for (int i = 0; i < IGNORE_ASSEMBLIES.Length; ++i) {
                    if (assembly.FullName.StartsWith(IGNORE_ASSEMBLIES[i])) {
                        found = true;
                        break;
                    }
                }

                //If the assembly isn't being skipped, process its values
                if (!found) {
                    //Add to the later search collection
                    toCheck.Add(assembly);

                    //Add all of the types included in it to the identified list
                    foreach (Type type in assembly.GetTypes()) {
                        //Add this type if it is a component or interface
                        if (type.IsInterface ||
                            componentType.IsAssignableFrom(type)) { 
                            identified.Add(new SortType {
                                displayName = type.FullName.Replace('.', '/').Replace('+', '/'),
                                type = type
                            });
                        }
                    }
                }
            }

            //Stash the display assemblies for later checking
            SEARCH_ASSEMBLIES = toCheck.ToArray();

            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////----------------------Setup the Generic Menu Type Selection---------------------//////////
            ////////////////////////////////////////////////////////////////////////////////////////////////////

            //Sort the identified types based on their display name
            identified.Sort((l, r) => l.displayName.CompareTo(r.displayName));

            //Setup the generic menu with all of the selectable type objects
            TYPE_SELECTION_MENU = new GenericMenu();
            for (int i = 0; i < identified.Count; ++i) {
                TYPE_SELECTION_MENU.AddItem(new GUIContent(identified[i].displayName), false, (object type) => {
                    if (onTypeSelected != null) onTypeSelected((Type)type);
                    onTypeSelected = null;
                }, identified[i].type);
            }
        }

        /// <summary>
        /// Initialise this objects internal information
        /// <summary>
        private void Awake() {
            titleContent = new GUIContent("Hierarchy Coloring");

            //Create the GUI Content objects for the different display elements
            displayHeader = new GUIContent("Display Settings");
            multiLabel = new GUIContent("Allow Multicolored", "Flags if multiple colors are allowed to be used or if only the first applicable to an object should be used");
            gradientLabel = new GUIContent("Use Gradient", "Flags if a gradient should be used if more then one color value should be applied to an option");
            indentLabel = new GUIContent("Label Indentation", "The number of pixels the colored labels will be indented when displayed");
            processingHeader = new GUIContent("Processing Elements");
            colorLabel = new GUIContent("Highlight", "The color that will be used to fill the line of hierarchy objects that meet these criteria");
            overrideLabel = new GUIContent("Override Text Color", "Flags if the text color that is used to display the element should be overridden to use the set supplied value. In the event of multiple elements being applied, the first will be used");
            textColLabel = new GUIContent("Text Color", "The color that will be applied to the hierarchy element to help distinguish it from the background color");
        }

        /// <summary>
        /// Render the window UI controls to the display area
        /// </summary>
        private void OnGUI() {
            //Begin waiting for changes to occur within the Inspector
            EditorGUI.BeginChangeCheck();

            //Display the basic settings 
            EditorGUILayout.LabelField(displayHeader, EditorStyles.boldLabel);

            //Display the toggle options
            HierarchyColoringProcessor.allowMultiColored = EditorGUILayout.Toggle(
                multiLabel,
                HierarchyColoringProcessor.allowMultiColored
            );
            HierarchyColoringProcessor.useGradient = EditorGUILayout.Toggle(
                gradientLabel, 
                HierarchyColoringProcessor.useGradient
            );

            //Display an option for modifying the indentation of the displayed elements
            HierarchyColoringProcessor.labelIndentation.x = Mathf.Max(0f, EditorGUILayout.FloatField(
                indentLabel,
                HierarchyColoringProcessor.labelIndentation.x
            ));

            //Add some buffer space
            EditorGUILayout.Space();

            //Check that the list is valid for being displayed
            if (processElementsList == null) processElementsList = CreateReorderableList();

            //Display the re-orderable list of elements to be processed
            processScrollProgress = EditorGUILayout.BeginScrollView(processScrollProgress); {
                processElementsList.DoLayoutList();
            } EditorGUILayout.EndScrollView();

            //If anything changes then the values can be updated and the cache cleared
            if (EditorGUI.EndChangeCheck()) {
                HierarchyColoringProcessor.SaveColoringPreferences();
                HierarchyColoringProcessor.ClearDisplayCache();
            }
        }

        /// <summary>
        /// Create a reorderable list that will display the various coloring elements 
        /// </summary>
        /// <returns>Returns a Reorderable list that can be displayed with specified values</returns>
        private ReorderableList CreateReorderableList() {
            //Store a reference to the array of objects that will be processed
           List<ColoringValues> values = HierarchyColoringProcessor.toProcess;

            //Create the list of objects to be displayed
            ReorderableList list = new ReorderableList(values, typeof(ColoringValues), true, true, true, true);

            //Override the drawing elements to use the property defaults
            list.elementHeightCallback += (index) => (EditorGUIUtility.singleLineHeight + LINE_BUFFER_SPACE) * (values[index].overrideTextColor ? 4.05f : 3.05f);
            list.drawHeaderCallback = (Rect rect) => EditorGUI.LabelField(rect, processingHeader);
            list.drawElementCallback += (Rect rect, int index, bool isActive, bool isFocused) => {
                //Display the enumeration option as the first selection option
                ETextType newType = (ETextType)EditorGUI.EnumPopup(
                    new Rect(rect.x, rect.y, rect.width * .2f, EditorGUIUtility.singleLineHeight), 
                    values[index].textType
                );

                //If the option is changed, clear the previous text value
                if (newType != values[index].textType) {
                    //Set the new type value
                    values[index].textType = newType;

                    //Set a default text value based on the selection type
                    switch (newType) {
                        case ETextType.Tag: values[index].text = "Untagged"; break;
                        case ETextType.Layer: values[index].text = "0"; break;
                        default: values[index].text = string.Empty; break;
                    }
                }
                
                //Switch based on the type that is to be displayed
                switch (newType) {
                    //Simple options that can be easily processed
                    case ETextType.Tag:
                        values[index].text = EditorGUI.TagField(
                            new Rect(rect.x + rect.width * .225f, rect.y, rect.width * .775f, EditorGUIUtility.singleLineHeight),
                            values[index].text
                        );
                        break;
                    case ETextType.Layer:
                        values[index].text = EditorGUI.LayerField(
                            new Rect(rect.x + rect.width * .225f, rect.y, rect.width * .775f, EditorGUIUtility.singleLineHeight),
                            int.Parse(values[index].text)
                        ).ToString();
                        break;
                    case ETextType.Name:
                        values[index].text = EditorGUI.DelayedTextField(
                            new Rect(rect.x + rect.width * .225f, rect.y, rect.width * .775f, EditorGUIUtility.singleLineHeight),
                            values[index].text
                        );
                        break;

                    //Check for type entering or menu selection
                    case ETextType.Type:
                        //Display a text field for manual entry of a search type
                        string newTypeText = EditorGUI.DelayedTextField(
                            new Rect(rect.x + rect.width * .225f, rect.y, rect.width * .725f, EditorGUIUtility.singleLineHeight),
                            values[index].text
                        );

                        //If the type text changes, try to find a type that matches
                        if (newTypeText != values[index].text) {
                            //Try to find a type that matches what was entered
                            for (int i = 0; i < SEARCH_ASSEMBLIES.Length; ++i) {
                                Type found = SEARCH_ASSEMBLIES[i].GetType(newTypeText, false);
                                if (found != null) {
                                    newTypeText = MinifyTypeAssemblyName(found);
                                    break;
                                }
                            }

                            //Assign the new type text to this entry
                            values[index].text = newTypeText;
                        }

                        //Display a button that can be used to quick select possible types
                        if (EditorGUI.DropdownButton(new Rect(rect.x + rect.width * .95f, rect.y, rect.width * .05f, EditorGUIUtility.singleLineHeight), GUIContent.none, FocusType.Passive)) {
                            //Store a lambda-catchable object reference that can be modified
                            ColoringValues toModify = values[index];

                            //Create the callback for the generic menu
                            onTypeSelected = type => toModify.text = MinifyTypeAssemblyName(type);

                            //Show the type options that are usable
                            TYPE_SELECTION_MENU.ShowAsContext();
                        }

                        break;
                }

                //Display the color value that will be used to highlight the nominated objects
                values[index].color = EditorGUI.ColorField(
                    new Rect(rect.x, rect.y + (EditorGUIUtility.singleLineHeight + LINE_BUFFER_SPACE) * 1f, rect.width, EditorGUIUtility.singleLineHeight),
                    colorLabel,
                    values[index].color
                );

                //Display the toggle field for setting the text override color
                bool isOverridden = values[index].overrideTextColor;
                values[index].overrideTextColor = EditorGUI.Toggle(
                    new Rect(rect.x, rect.y + (EditorGUIUtility.singleLineHeight + LINE_BUFFER_SPACE) * 2f, rect.width, EditorGUIUtility.singleLineHeight),
                    overrideLabel,
                    values[index].overrideTextColor
                );

                //Check if the text color override should be displayed for modification
                if (isOverridden) {
                    values[index].textColor = EditorGUI.ColorField(
                        new Rect(rect.x, rect.y + (EditorGUIUtility.singleLineHeight + LINE_BUFFER_SPACE) * 3f, rect.width, EditorGUIUtility.singleLineHeight),
                        textColLabel,
                        values[index].textColor
                    );
                }
            };

            return list;
        }

        /// <summary>
        /// Trim down the supplied type assembly name for simple storage
        /// </summary>
        /// <param name="type">A type defintion that is to be trimmed down to its basic information</param>
        /// <returns>Returns the supplied string without additional assembly information</returns>
        /// <remarks>
        /// This function is intended to handle strings produced by the Type.AssemblyQualifiedName property
        /// 
        /// Implementation is taken from the deserialized Argument Cache object within Unity
        /// Reference document https://github.com/jamesjlinden/unity-decompiled/blob/master/UnityEngine/UnityEngine/Events/ArgumentCache.cs
        /// </remarks>
        private static string MinifyTypeAssemblyName(Type type) {
            //Check that there is a unity object type name to clean
            if (type == null) return string.Empty;

            //Get the assembly name of the object
            string typeAssembly = type.AssemblyQualifiedName;

            //Find the point to cut off the type definition
            int point = int.MaxValue;

            //Find the points that are usually included within an assembly type name
            int buffer = typeAssembly.IndexOf(", Version=");
            if (buffer != -1) point = Math.Min(point, buffer);
            buffer = typeAssembly.IndexOf(", Culture=");
            if (buffer != -1) point = Math.Min(point, buffer);
            buffer = typeAssembly.IndexOf(", PublicKeyToken=");
            if (buffer != -1) point = Math.Min(point, buffer);

            //If nothing was found, type is fine
            if (point == int.MaxValue) return typeAssembly;

            //Substring the type to give the shortened version
            return typeAssembly.Substring(0, point);
        }

        //PUBLIC

        /// <summary>
        /// Open this window and bring it into the foreground
        /// </summary>
        /// <returns>Returns a reference to the HierarchyColoring window instance</returns>
        [MenuItem("Window/Hierarchy Coloring %#H")]
        public static HierarchyColoringWindow Init() {
            HierarchyColoringWindow window = GetWindow<HierarchyColoringWindow>();
            window.Show();
            return window;
        }
    }
}