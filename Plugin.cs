// Copyright (c) 2024/2025 Jo912345/J0912345. released under the MIT license (see LICENSE.txt file).

using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BoplFixedMath;
using static HitBoxVisualizerPlugin.hitboxVisualizerLineStyling;
using BepInEx.Configuration;


namespace HitBoxVisualizerPlugin
{

    [BepInPlugin("com.jo912345.hitboxVisualizePlugin", "HitBoxVisualizer", PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        // int is the instance ID
        public static Dictionary<int, DPhysicsBox> DPhysBoxDict = [];
        public static Dictionary<int, DPhysicsCircle> DPhysCircleDict = [];
        public static Dictionary<int, Circle> CirlceDict = [];

        public static listOfLineHolderGameObjs poolOfLineHolderGameObjs = new listOfLineHolderGameObjs();

        public ConfigEntry<float> CONFIG_drawingThickness;

        public ConfigEntry<String> CONFIG_rectColors;
        public ConfigEntry<String> CONFIG_circleColors;
        public ConfigEntry<String> CONFIG_disabledColors;

        public static List<object> externalLogMessageQueue = [];
        public static float drawingThickness = 0.5f;
        public static int circleDrawingMinAmountOfLines = 14;

        public enum hitboxVisRenderImplementation
        {
            unityLineRenderObj
        }
        public static hitboxVisRenderImplementation currHitBoxVisRendImplementation = hitboxVisRenderImplementation.unityLineRenderObj;


        private readonly Harmony harmony = new Harmony("com.jo912345.hitboxVisualizePlugin");

        public void Awake()
        {
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            LineDrawing.setUpLineRendererMaterialToDefault();
            poolOfLineHolderGameObjs.setAllLineRendererMaterials(LineDrawing.lineRendererBaseMaterial);

            loadConfigValues();
            harmony.PatchAll();
            Logger.LogInfo("all `DPhysicsBox`es/`DPhysicsCircle`s should now have their hitboxes drawn!");
        }

        public void loadConfigValues()
        {
            // drawing settings
            CONFIG_drawingThickness = Config.Bind(
                "Drawing Settings",
                "drawingThickness",
                0.2f,
                "How thick should hitbox lines be drawn? Note that this value is in unity world units and not pixels. For reference, 0.2 is relatively thin, and 0.5 is large.\n" +
                "Rectangular hitboxes are now always drawn accurately, never extending past the real hitbox edge (excluding small artifacts caused by a lack of anti-aliasing)."
            );

            // line color settings
            CONFIG_rectColors = Config.Bind(
                "Line Color Settings",
                "rectangleColors",
                "#FF0000CC,#FFEB04CC,#00FF00CC,#0000FFCC",
                "What colors should rectangles use? Colors are separated by commas. Any colors after the 4th option will be ignored.\n" +
                "You can use #RGB, #RRGGBB, #RGBA, or #RRGGBBAA formatting, or one of the color words specified here:\n" +
                "https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html");
            CONFIG_circleColors = Config.Bind(
                "Line Color Settings",
                "circleColors",
                "#FF0000CC,#FFEB04CC,#00FF00CC,#0000FFCC,#FF00FFCC",
                "What colors should circles use? Colors are separated by commas. You may only use up to 7 colors per the limitations of Unity's `Gradient` class.\n" +
                "The first color is also used as the last color to loop the gradient.\n" +
                "You can use #RGB, #RRGGBB, #RGBA, or #RRGGBBAA formatting, or one of the color words specified here:\n" +
                "https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html");
            CONFIG_disabledColors = Config.Bind(
                "Line Color Settings",
                "disabledColors",
                "#000000CC,#FFFFFFCC,#000000CC,#FFFFFFCC",
                "What colors should disabled objects use? This includes rectanges and circles. Colors are separated by commas.\n" +
                "You may only use up to 7 colors (the gradient is looped), per the limitations of Unity's `Gradient` class.\n" +
                "You can use #RGB, #RRGGBB, #RGBA, or #RRGGBBAA formatting, or one of the color words specified here:\n" +
                "https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html");

            drawingStyleToLineColors[lineDrawingStyle.defaultColors] = loadConfigColorsFromString(CONFIG_rectColors.Value, "rectangleColors",
                drawingStyleToLineColors[lineDrawingStyle.defaultColors], false);

            drawingStyleToLineColors[lineDrawingStyle.circleColors] = loadConfigColorsFromString(CONFIG_circleColors.Value, "circleColors",
                drawingStyleToLineColors[lineDrawingStyle.circleColors]);

            drawingStyleToLineColors[lineDrawingStyle.disabledPhys] = loadConfigColorsFromString(CONFIG_disabledColors.Value, "disabledColors",
                drawingStyleToLineColors[lineDrawingStyle.disabledPhys]);

            drawingThickness = CONFIG_drawingThickness.Value;
        }
        public List<Color> loadConfigColorsFromString(String listString, String categoryName, List<Color> normalColors, bool gradientColorLimit = true)
        {
            String[] colorStrings = listString.Split(',');
            List<Color> colorList = new List<Color>();
            Color curColor = new Color();
            foreach (String colorString in colorStrings)
            {
                bool success = ColorUtility.TryParseHtmlString(colorString, out curColor);
                if (!success)
                {
                    Logger.LogError("color string \""+ colorString + "\" is invalid, falling back to default colors for " + categoryName + " instead. " +
                        "Check the config file or https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html for formatting options.");
                    return normalColors;
                }
                colorList.Add(curColor);
            }
            if (gradientColorLimit && colorList.Count() > 7)
            {
                Logger.LogError("color string \"" + listString + "\" is invalid, falling back to default colors for " + categoryName + " instead. " +
                    "Using more than 7 colors (the gradient is looped) in a gradient is not supported, due to a unity limitation.");
                return normalColors;
            }
            return colorList;
        }

        public void Start()
        {
            // redundant, required for bopl 2.4.3+ for a non-zero minCapacity.
            // extra game objects are cleaned up at boot by unity in newer versions.
            poolOfLineHolderGameObjs = new listOfLineHolderGameObjs();
        }

        public void OnDestroy()
        {
            harmony.UnpatchSelf();
            Logger.LogError("hitboxVisualizer has been unloaded. (if you see this when starting the game, it's likely that `HideManagerGameObject = false` in `BepInEx.cfg`. please enable it!)");
        }

        public static void AddExternalLogPrintToQueue(object data)
        {
            //if (externalLogMessageQueue.Count+1 > externalLogMessageQueue.Capacity)
            //{
            //    return;
            //}

            if (externalLogMessageQueue == null)
            {
                externalLogMessageQueue = [];
            }
            externalLogMessageQueue.Add(data);
        }
        private void PrintAllExternalLogsInQueue(/*object sender*/)
        {
            //if (externalLogMessageQueue.Count > externalLogMessageQueue.Capacity)
            //{
            //    Logger.LogWarning("INTERNAL LOG QUEUE OVERFLOW! EXTRA MESSAGES WILL HAVE BEEN DISCARDED!");
            //}
            for (int i = 0; i < externalLogMessageQueue.Count; i++)
            {
                Logger.LogInfo(String.Concat("ASYNC* MESSAGE DUMP: ", externalLogMessageQueue[i].ToString()));
            }
            externalLogMessageQueue = [];
        }

        // using both Update() and LateUpdate() might(?) increase accuracy for weird interframe stuff but it's genuinely hard to tell. 
        // it would catch hitboxes that exist during `Update()` but don't during `LateUpdate` (if any like that exist).
        // I think they do, but in that case the game doesn't seem to fully process them. meteor can be seen deleting(?) hitboxes for a frame sometimes,
        // including seemingly deleting a player's hitbox. after adding code to list when stuff is removed though it doesn't look like it actually is?
        // I'm tired of looking at clips frame by frame and there aren't any (good) TAS tools for this game (I've tested, libTAS has issues).
        // maybe I'll make a replay editor someday...
        /*public void Update()
        {
            updateHitboxes();
        }*/

        // the lineRenderer lags behind if LateUpdate isn't used
        public void LateUpdate()
        {
            updateHitboxes();
        }

        public void updateHitboxes()
        {
            PrintAllExternalLogsInQueue();

            var ListOflineGroupTuple = calculateHitBoxShapeComponentLines(DPhysBoxDict, DPhysCircleDict);
            // circles already have very little distortion (likely due to their much shallower turns at each point)
            // and would also cost a ton of extra line holder game objects to render with 1 game object per line.
            // rects
            var HitboxComponentLines_NoDistortion = ListOflineGroupTuple.Item1;
            // circles
            var HitboxComponentLines = ListOflineGroupTuple.Item2;


            LineDrawing.drawLinesLineRender(HitboxComponentLines);
            LineDrawing.drawLineGroupAsSplitIntoIndividualLines(HitboxComponentLines_NoDistortion);
        }


        public Tuple<List<hitboxLineGroup>, List<hitboxLineGroup>> calculateHitBoxShapeComponentLines(Dictionary<int, DPhysicsBox> inputDPhysBoxDict, Dictionary<int, DPhysicsCircle> inputDPhysCircleDict)
        {
            // rects
            var newHitboxLineGroups_NoDistortion = new List<hitboxLineGroup> ();
            // circles
            var newHitboxLineGroups_DistortionAllowed = new List<hitboxLineGroup>();

            // CALCULATE RECTS
            for (int i = 0; i < inputDPhysBoxDict.Values.ToList().Count; i++)
            {
                var currBox = inputDPhysBoxDict.Values.ToList()[i];

                if (currBox == null)
                {
                    Logger.LogWarning("inputDPhysBoxDict.Values.ToList()[i] == null, removing from array. a round probably ended.");
                    inputDPhysBoxDict.Remove(inputDPhysBoxDict.Keys.ToList()[i]);
                    continue;
                }

                var boxOfCurrBox = currBox.Box();
                var boxScale = currBox.Scale;


                // via just staring into the decompiled code for a while, I realized I should try accessing the values from the physics engine.
                // accessing the value from there fixes beam's scaling issues
                var boxPointUp = boxOfCurrBox.up;
                var boxPointRight = boxOfCurrBox.right;
                var boxPointCenter = boxOfCurrBox.center;
                if (currBox.initHasBeenCalled)
                {
                    boxOfCurrBox = currBox.physicsBox;
                    boxPointRight  = boxOfCurrBox.right;
                    boxPointUp     = boxOfCurrBox.up;
                    boxPointCenter = boxOfCurrBox.center;
                    boxScale = (Fix)1;
                }

                // to make it approximately 500,000x more readable, there's some extra spacing so everything lines up nicely
                Vec2 boxPointUpLeft    = boxScale * (boxPointCenter + boxPointUp - boxPointRight);
                Vec2 boxPointDownLeft  = boxScale * (boxPointCenter - boxPointUp - boxPointRight);
                Vec2 boxPointUpRight   = boxScale * (boxPointCenter + boxPointUp + boxPointRight);
                Vec2 boxPointDownRight = boxScale * (boxPointCenter - boxPointUp + boxPointRight);

                hitboxVisualizerLine boxLineTop = new hitboxVisualizerLine(boxPointUpLeft, boxPointUpRight);
                hitboxVisualizerLine boxLineRight = new hitboxVisualizerLine(boxPointUpRight, boxPointDownRight);
                hitboxVisualizerLine boxLineBottom = new hitboxVisualizerLine(boxPointDownRight, boxPointDownLeft);
                hitboxVisualizerLine boxLineLeft = new hitboxVisualizerLine(boxPointDownLeft, boxPointUpLeft);
                    

                // make lines meet at the corners properly, and make their outer edge the real hitbox edge
                hitboxVisualizerLine[] box_lines = [boxLineTop, boxLineRight, boxLineBottom, boxLineLeft];
                for (int j = 0; j < box_lines.Length; j++)
                {
                    Vector2 p1 = (Vector2)box_lines[j].point1;
                    Vector2 p2 = (Vector2)box_lines[j].point2;
                    float angleRadPlus2Pi = Mathf.Atan2(p2.y - p1.y, p2.x - p1.x) + Mathf.PI/2;
                    box_lines[j].point1.x = box_lines[j].point1.x - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Cos((float)angleRadPlus2Pi);
                    box_lines[j].point1.y = box_lines[j].point1.y - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Sin((float)angleRadPlus2Pi);
                    box_lines[j].point2.x = box_lines[j].point2.x - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Cos((float)angleRadPlus2Pi);
                    box_lines[j].point2.y = box_lines[j].point2.y - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Sin((float)angleRadPlus2Pi);
                }

                newHitboxLineGroups_NoDistortion.Add(new hitboxLineGroup(
                    [
                    boxLineTop,
                    boxLineRight,
                    boxLineBottom,
                    boxLineLeft,
                    ],
                    hitboxLineGroup.pickLineStyling(currBox),
                    currBox.gameObject,
                    drawingThickness));
            }

            // CALCULATE CIRCLES
            //for (int i = 0; i < inputDPhysCircleDict.Count /*- 1*/; i++)
            for (int i = 0; i < inputDPhysCircleDict.Values.ToList().Count /*- 1*/; i++)
            {
                var currCircle = inputDPhysCircleDict.Values.ToList()[i];

                if (currCircle == null)
                {
                    inputDPhysCircleDict.Remove(inputDPhysCircleDict.Keys.ToList()[i]);
                    Logger.LogWarning("inputDPhysCircleDict.Values.ToList()[i] == null, removing from array. a round probably ended.");
                    continue;
                }

                var currCircleShape = currCircle.Circle();
                Fix circleRadius = currCircleShape.radius * currCircle.Scale;
                Fix circleX = currCircleShape.Pos().x;
                Fix circleY = currCircleShape.Pos().y;

                // for some reason the physics engine and class instance have separate varibles for the same property.
                if (currCircle.initHasBeenCalled)
                {
                    var physEngineCircleObj = DetPhysics.Get().circles.colliders[DetPhysics.Get().circles.ColliderIndex(currCircle.pp.instanceId)];
                    circleRadius = physEngineCircleObj.radius;
                    circleX =      physEngineCircleObj.Pos().x;
                    circleY =      physEngineCircleObj.Pos().y;
                }

                circleRadius -= (Fix)drawingThickness / (Fix)2;

                int circleLineAmount = circleDrawingMinAmountOfLines + (int)(circleRadius * (Fix)4);
                //Logger.LogInfo("circle radius: " + circleRadius.ToString() + " | circleLineAmount: " + circleLineAmount.ToString());
                float angleDifferencePerIteration = 360f / (float)circleLineAmount;

                List<hitboxVisualizerLine> currCircleLines = [];
                // setup the initial first position so the loop can be simpler
                // also yeah this just simplies down due to cos(0) = 1 and sin(0) = 0
                Vec2 nextStartingCirclePoint = new Vec2(circleX+circleRadius, circleY);

                // yes, j = 1 because nextStartingCirclePoint is already set
                // we also add <strikethrough>+1</strikethrough> for circleDrawing_AmountOfLines because j starts at 1 instead of 0
                //  I'm not sure why +2 fixes the last points not being drawn sometimes? this is required after I made the amount of lines scale with the radius.
                for (int j = 1; j < circleLineAmount + 2; j++)
                {
                    float angle = j * angleDifferencePerIteration * Mathf.Deg2Rad;
                    
                    Vec2 newCirclePoint = new Vec2(circleX + (circleRadius * (Fix)Mathf.Cos(angle)), circleY + (circleRadius * (Fix)Mathf.Sin(angle)));
                    currCircleLines.Add(new hitboxVisualizerLine(nextStartingCirclePoint, newCirclePoint));

                    nextStartingCirclePoint = newCirclePoint;
                }

                newHitboxLineGroups_DistortionAllowed.Add(new hitboxLineGroup(currCircleLines, hitboxLineGroup.pickLineStyling(currCircle), currCircle.gameObject, drawingThickness));
            }

            return Tuple.Create(newHitboxLineGroups_NoDistortion, newHitboxLineGroups_DistortionAllowed);
        }
    }

    [HarmonyPatch(typeof(DPhysicsBox))]
    class Patch_AddDPhysBoxBoundsTracking
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DPhysicsBox.Initialize))]
        [HarmonyPatch(nameof(DPhysicsBox.Init))]
        [HarmonyPatch(nameof(DPhysicsBox.ManualInit))]
        [HarmonyPatch(nameof(DPhysicsBox.Awake))]
        static void Postfix(DPhysicsBox __instance)
        {
            var instanceID = __instance.GetInstanceID();

            if (Plugin.DPhysBoxDict.ContainsKey(instanceID))
            {
                return;
            }
            Plugin.AddExternalLogPrintToQueue("adding instanceID DPhysicsBox to list:" + instanceID);
            Plugin.DPhysBoxDict.Add(instanceID, __instance);
        }
    }

    [HarmonyPatch(typeof(DPhysicsCircle))]
    class Patch_AddDPhysCircleBoundsTracking
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DPhysicsCircle.Initialize))]
        [HarmonyPatch(nameof(DPhysicsCircle.Init))]
        [HarmonyPatch(nameof(DPhysicsCircle.ManualInit))]
        [HarmonyPatch(nameof(DPhysicsCircle.Awake))]
        static void Postfix(DPhysicsCircle __instance)
        {
            var instanceID = __instance.GetInstanceID();

            if (Plugin.DPhysCircleDict.ContainsKey(instanceID))
            {
                return;
            }

            Plugin.AddExternalLogPrintToQueue("adding instanceID DPhysicsCircle to list:" + instanceID);
            Plugin.DPhysCircleDict.Add(instanceID, __instance);
        }
    }

    // note leftovers will get cleaned up by other code that will delete null instances if they end up in the dict anyway
    // mainly that seems to happen at the end of rounds but in case this function either didn't exist or wasn't working everything would still get cleaned eventually
    [HarmonyPatch(typeof(MonoUpdatable))]
    class Patch_MonoUpdatable_AddDPhysObjDeleteHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DPhysicsBox.OnDestroyUpdatable))]
        [HarmonyPatch(nameof(DPhysicsCircle.OnDestroyUpdatable))]
        static void Postfix_addPhysBoxDelHook(MonoUpdatable __instance)
        {
            if (__instance == null)
            {
                return;
            }
            var instanceClassName = __instance.GetScriptClassName();

            if (instanceClassName == "DPhysicsBox")
            {
                Plugin.DPhysBoxDict.Remove(__instance.GetInstanceID());
                var instanceID = __instance.GetInstanceID();
                Plugin.AddExternalLogPrintToQueue("removing instanceID DPhysicsBox from list:" + instanceID);
            }
            else if (instanceClassName == "DPhysicsCircle")
            {
                Plugin.DPhysCircleDict.Remove(__instance.GetInstanceID());
                var instanceID = __instance.GetInstanceID();
                Plugin.AddExternalLogPrintToQueue("removing instanceID DPhysicsCircle from list:" + instanceID);
            }
        }
    }

    public class listOfLineHolderGameObjs
    {
        public List<GameObject> gameObjsList = new List<GameObject>();
        public int minCapacity = 4;
        public int currUsedAmountOfGameObjs = 0;
        public Material lineMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));

        public void addGameObjects(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                var currGameObj = new GameObject();
                GameObject.DontDestroyOnLoad(currGameObj);
                LineRenderer lineRenderer = currGameObj.AddComponent(typeof(LineRenderer)) as LineRenderer;

                lineRenderer.material = lineMaterial;
                lineRenderer.sortingLayerID = SortingLayer.NameToID("PostProcessing");
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                gameObjsList.Add(currGameObj);
            }
        }

        public void setAllLineRendererMaterials(Material newMaterial)
        {
            for (int i = 0; i < gameObjsList.Count; i++)
            {
                gameObjsList[i].TryGetComponent(out LineRenderer lineRenderer);
                lineRenderer.material = newMaterial;
            }
        }

        public listOfLineHolderGameObjs(bool useLineDrawingClassMaterial = true)
        {
            if (useLineDrawingClassMaterial)
            {
                lineMaterial = LineDrawing.lineRendererBaseMaterial;
            }
            addGameObjects(minCapacity);
        }

        public void setLineRendererPropsAt(int objListIndex, Vector3 startPos, Vector3 endPos, float thickness, Color lineColor, bool objIsActive = true)
        {
            var currGameObj = gameObjsList[objListIndex];

            // if false this will also make the LineRenderer invisible
            currGameObj.SetActive(objIsActive);

            currGameObj.TryGetComponent(out LineRenderer lineRenderer);
            lineRenderer.SetPositions([startPos, endPos]);
            lineRenderer.startWidth = thickness;
            lineRenderer.endWidth   = thickness;
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor   = lineColor;
            lineRenderer.forceRenderingOff = false;
        }

        public void deleteGameObjectsAfterIndex(int baseIndex)
        {
            var amountOfObjsToDelete = gameObjsList.Count - baseIndex;
            for (int i = 0; i < amountOfObjsToDelete; i++)
            {
                var currGameObj = gameObjsList[baseIndex];
                gameObjsList.Remove(currGameObj);
                GameObject.Destroy(currGameObj);

            }
        }

        public void cleanUpOldLineRendererPositionsFromGameObjsAfter(int startIndex)
        {
            for (int i = startIndex; i < gameObjsList.Count; i++)
            {
                if (gameObjsList[i].TryGetComponent(out LineRenderer lineRenderer))
                {
                    lineRenderer.SetPositions([new Vector3(0, 0), new Vector3(0, 0)]);
                    lineRenderer.forceRenderingOff = true;
                }
            }
        }

        public void cleanUpAllLineRenderers()
        {
            for (int i = 0; i < gameObjsList.Count; i++)
            {
                gameObjsList[i].TryGetComponent(out LineRenderer lineRenderer);
                lineRenderer.SetPositions((Vector3[])[]);
                lineRenderer.forceRenderingOff = true;
            }
        }
    }

    public struct hitboxVisualizerLineStyling
    {
        // these are constant but the values in drawingStyleToLineColors get overwritten when the config's color settings load.
        // if the config is invalid these'll be used as a fallback.
        public static Color RedColor     = new Color(1, 0, 0, 1f);
        public static Color BlueColor    = new Color(0, 0, 1, 1f);
        public static Color GreenColor   = new Color(0, 1, 0, 1f);
        public static Color YellowColor  = new Color(1, 0.92f, 0.016f, 1f);
        public static Color MagentaColor = new Color(1, 0, 1, 1f);
        public static Color WhiteColor   = new Color(1, 1, 1, 1f);
        public static Color BlackColor   = new Color(0, 0, 0, 1f);
        // TODO: test using these instead of the pure white and black
        public static Color DarkGrey     = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static Color lightGrey    = new Color(0.8f, 0.8f, 0.8f, 1f);
        public enum lineDrawingStyle
        {
            defaultColors,
            disabledPhys,
            circleColors
        }
        public static Dictionary<lineDrawingStyle, List<Color>> drawingStyleToLineColors = new Dictionary<lineDrawingStyle, List<Color>>
        {
            {lineDrawingStyle.defaultColors, [RedColor, YellowColor, GreenColor, BlueColor, MagentaColor]},
            {lineDrawingStyle.disabledPhys, [BlackColor, WhiteColor]},
            {lineDrawingStyle.circleColors, [RedColor, YellowColor, GreenColor, BlueColor, MagentaColor]}
        };
    }

    public class hitboxVisualizerLine
    {

        public Vec2 point1;
        public Vec2 point2;
        public Color lineColor;
        public hitboxVisualizerLine(Vec2 point_1, Vec2 point_2)
        {
            point1 = point_1;
            point2 = point_2;
        }
    }
    // because the lineRenderer implementation uses SetPositions() every time it's called to update and SetPositions() removes the old value (aka what a setter does),
    // if the lines aren't grouped together per object, the lines that are drawn later will completely overwrite the old lines for that object.
    // this means we either can only have 1 line per object or have to give it a list of lines so it can SetPositions() with all of the lines as 1 set.
    // (later)
    // BUT THEN, APPARENLTLY LINERENDERER IS ACTUALLY TERRIBLE AT DRAWING LINES!
    // ANYTHING MORE COMPLEX THAN JUST POINT A TO POINT B CREATES HIGHLY UGLY DISTORTION!
    public class hitboxLineGroup
    {
        public float lineThickness;
        public List<hitboxVisualizerLine> hitboxVisualLines;
        // a reference some kind of GameObject needed for the lineRenderer implementation, as linerenderers themselves are game objects.
        public GameObject parentGameObj;
        public lineDrawingStyle lineGroupStyle;

        public hitboxLineGroup(List<hitboxVisualizerLine> hitboxLines, lineDrawingStyle lineStyle, GameObject parentGameObject, float lineWidth=0.5f)
        {
            if (parentGameObject == null)
            {
                return;
            }
            hitboxVisualLines = hitboxLines;
            parentGameObj = parentGameObject;
            lineThickness = lineWidth;
            lineGroupStyle = lineStyle;
            UpdateLineColorsToMatchStyle(lineStyle);
        }

        public static lineDrawingStyle pickLineStyling(IPhysicsCollider DPhysObj)
        {
            if (!DPhysObj.enabled)
            {
                return lineDrawingStyle.disabledPhys;
            }
            if (DPhysObj is DPhysicsCircle)
            {
                return lineDrawingStyle.circleColors;
            }
            return lineDrawingStyle.defaultColors;
        }

        public void UpdateLineColorsToMatchStyle(lineDrawingStyle lineStyle)
        {
            lineGroupStyle = lineStyle;
            var colorIndex = 0;
            var lineColors = drawingStyleToLineColors[lineGroupStyle];
            for (int i = 0; i < hitboxVisualLines.Count; i++)
            {
                if (colorIndex > lineColors.Count - 1)
                {
                    colorIndex = 0;
                }
                var currLine = hitboxVisualLines[i];

                currLine.lineColor = lineColors[colorIndex];

                colorIndex++;
            }
        }

        public Vector3[] getListOfComponentPoints()
        {
            List<Vector3> linePointsArray = [];

            linePointsArray.Add(new Vector3((float)hitboxVisualLines[0].point1.x, (float)hitboxVisualLines[0].point1.y, 0));

            for (int i = 0; i < hitboxVisualLines.Count-1; i++)
            {
                var currLine = hitboxVisualLines[i];
                linePointsArray.Add(new Vector3((float)currLine.point1.x, (float)currLine.point1.y, 0));
                //linePointsArray.Add(new Vector3((float)currLine.point2.x, (float)currLine.point2.y, 0));
            }
            return linePointsArray.ToArray();
        }

        public Gradient getLineGradientForLineColors()
        {
            List<GradientColorKey> lineGradientColors = [];
            List<GradientAlphaKey> lineGradientAlphas = [];

            var lineColors = drawingStyleToLineColors[lineGroupStyle];
            float percentOfLinePerOneColorSegment = 1f / (float)lineColors.Count();

            for (int i = 0; i < lineColors.Count(); i++)
            {
                float gradientLinePos = i * percentOfLinePerOneColorSegment;
                lineGradientColors.Add(new GradientColorKey(lineColors[i], gradientLinePos));
                lineGradientAlphas.Add(new GradientAlphaKey(lineColors[i].a, gradientLinePos));
            }
            lineGradientColors.Add(new GradientColorKey(lineColors[0], 1));
            lineGradientAlphas.Add(new GradientAlphaKey(lineColors[0].a, 1));

            Gradient lineGradient = new Gradient();
            lineGradient.SetKeys(lineGradientColors.ToArray(), lineGradientAlphas.ToArray() );
            lineGradient.mode = GradientMode.PerceptualBlend;
            return lineGradient;
        }
    }
    public class LineDrawing
    {
        public static Material lineRendererBaseMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));

        public static void setUpLineRendererMaterialToDefault()
        {
            lineRendererBaseMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            lineRendererBaseMaterial.SetOverrideTag("RenderType", "Fade");
            lineRendererBaseMaterial.renderQueue = 5000;
            lineRendererBaseMaterial.SetInt("_ZWrite", 1);
            lineRendererBaseMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        public static void drawLinesLineRender(List<hitboxLineGroup> lineGroups)
        {
            for (int j = 0; j < lineGroups.Count; j++)
            {
                var lineGroup = lineGroups[j];
                var lineParentObj = lineGroup.parentGameObj;

                if (lineParentObj == null)
                {
                    Plugin.AddExternalLogPrintToQueue("lineParentObj is null");
                    continue;
                }

                LineRenderer lineRenderer;
                if (lineParentObj.TryGetComponent<LineRenderer>(out LineRenderer curLineRederer))
                {
                    lineRenderer = curLineRederer;
                }
                else
                {
                    lineRenderer = lineParentObj.AddComponent<LineRenderer>();
                }

                Vector3[] newPositions = lineGroup.getListOfComponentPoints();
                // according to the unity project as decompiled by assetRipper, PostProcessing is the highest sorting layer in v2.3.4
                lineRenderer.sortingLayerID = SortingLayer.NameToID("PostProcessing");
                lineRenderer.loop = true;
                lineRenderer.material = lineRendererBaseMaterial;
                lineRenderer.colorGradient = lineGroup.getLineGradientForLineColors();
                lineRenderer.startWidth = lineGroup.lineThickness;
                lineRenderer.endWidth = lineGroup.lineThickness;
                lineRenderer.positionCount = newPositions.Length;
                lineRenderer.material.renderQueue = 4999;
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;


                lineRenderer.SetPositions(newPositions);
                // mainly helps *SOME OF* the remove ugly distortion caused by LineRenderer
                lineRenderer.Simplify(0);
            }
        }

        // because of how LineRenderer works, anything more complicated than a direct line from point A to B will start having really ugly distortion effects.
        // at least for stuff like rectangles. circles seem to suffer from it less and so will use the other function.
        // using LineRenderer.Simplify() and setting the line thickness set lower can help but the distortion is apparently impossible to fully remove
        // ============================================
        // so the solution is to create a bunch of LineRenderers and just use 1 line per linerenderer, as a simple point A to point B line doesn't have the distortion issue.
        // this also means that a bunch of holder child objects must be created because GameObject can only hold 1 LineRenderer at a time
        public static void drawLineGroupAsSplitIntoIndividualLines(List<hitboxLineGroup> lineGroups)
        {
            int amountOfUsedHolderObjs = 0;
            listOfLineHolderGameObjs holderGameObjs = Plugin.poolOfLineHolderGameObjs;

            for (int i = 0; i < lineGroups.Count; i++) {

                var currLineGroup = lineGroups[i];
                var lineParentObj = currLineGroup.parentGameObj;

                if (lineParentObj == null)
                {
                    Plugin.AddExternalLogPrintToQueue("lineParentObj is null");
                    continue;
                }

                var linesList = currLineGroup.hitboxVisualLines;
                var amountOfLinesInGroup = linesList.Count;

                if (amountOfUsedHolderObjs + amountOfLinesInGroup > holderGameObjs.gameObjsList.Count)
                {
                    holderGameObjs.addGameObjects(amountOfLinesInGroup);
                }
                for (int j = 0; j < amountOfLinesInGroup; j++)
                {
                    var line = linesList[j];
                    holderGameObjs.setLineRendererPropsAt(amountOfUsedHolderObjs, (Vector3)line.point1, (Vector3)line.point2, currLineGroup.lineThickness, line.lineColor, lineParentObj.activeSelf);
                    amountOfUsedHolderObjs++;
                }
            }
            // clean up any unused game objects
            if ((amountOfUsedHolderObjs < holderGameObjs.gameObjsList.Count) && (holderGameObjs.gameObjsList.Count > holderGameObjs.minCapacity))
            {
                holderGameObjs.deleteGameObjectsAfterIndex(holderGameObjs.minCapacity);
            }
            // clear LineRenderer positions on any unused gameObjects, so that we don't get old lines still displaying on screen
            holderGameObjs.cleanUpOldLineRendererPositionsFromGameObjsAfter(amountOfUsedHolderObjs);

            
        }
    }
}
