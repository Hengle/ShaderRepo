using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static SDFUtils;

// ����һ��Unity�༭�����Զ��崰�ڣ���������SDF��ͼ
// �ô��ڿ���ͨ���˵�����Window/SDF Generator��
public class SDFGenerator_Editor : EditorWindow
{
    enum SDFMode
    {
        UDF_bin,    // ��Զ�ֵ����ͼ������UDF
        SDF_bin,    // ��Զ�ֵ����ͼ������SDF
        SDF_height, // ��Ը߶�ͼ����SDF
        SDF_face,   // ��Զ���Ԫ�����沿��ӰͼƬ����SDF 
    }


    public static SDFGenerator_Editor window;
    public SDFGenerator sdfGenerator = null;


    private SDFMode mode = SDFMode.SDF_face;
    private SDFChannel channel = SDFChannel.GrayScale;

    private Texture2D waitForSDF = null;
    private Texture2D waitForSDF_InProj = null;
    private Texture2D afterPreprocessTexture = null;
    private Texture2D afterSdfTexture = null;
    private RenderTexture originalSdfTexture = null;

    private int sdfWidth = 256;
    private int sdfHeight = 256;
    private bool strectchToSquare = false;
    private int pixelCountToArriveOne = 256;

    // ��ֵ��UDF�Ĳ���
    private float binThreshold = 0.5f;
    private bool binInvert = false;

    // �沿SDF�Ĳ���
    private List<string> shadowPathList = null;
    private List<Texture2D> shadowTexList = null;

    // UI
    bool settingsPosition= true;
    bool previewPosition = true;
    bool singlePosition = true;


    // ����һ���˵�����ڴ�SDF����������
    [MenuItem("Window/SDF Generator")]
    public static void ShowWindow()
    {
        window = GetWindow<SDFGenerator_Editor>();
        window.titleContent = new GUIContent("SDF Generator");
        window.sdfGenerator = new SDFGenerator();
        window.Show();
    }

    // �ڴ����л���GUI
    private void OnGUI()
    {
        bool needRefreshRT = false;
        // �ⲿ�������ô��ڣ���������ǩҳ���ֱ���������SDF�������Ĳ�ͬģʽ�Ͳ���
        settingsPosition = EditorGUILayout.BeginFoldoutHeaderGroup(settingsPosition, "Settings");
        if (settingsPosition)
        { 
            mode = (SDFMode)EditorGUILayout.EnumPopup("Mode", mode);
            OnCommonGUI(ref needRefreshRT);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(10);

        // ���ݲ�ͬ��ģʽ����ʾ��ͬ�����ý���

        singlePosition = EditorGUILayout.BeginFoldoutHeaderGroup(singlePosition, "Advanced Settings");
        if (singlePosition)
        {
            switch (mode)
            {
                case SDFMode.UDF_bin:
                case SDFMode.SDF_bin:
                    OnBinGUI(ref needRefreshRT);
                    break;
                case SDFMode.SDF_height:
                    OnHeightGUI();
                    break;
                case SDFMode.SDF_face:
                    OnFaceGUI(ref needRefreshRT);
                    break;
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(10);

        // �ⲿ������Ԥ��ѡ�е�ͼƬ
        previewPosition = EditorGUILayout.BeginFoldoutHeaderGroup(previewPosition, "Preview");
        if (previewPosition)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Selected Image Preview");
            if (afterPreprocessTexture != null)
            {
                // ���ߴ�Ϊ256x256�������쵽��Ӧ����
                GUILayout.Label(afterPreprocessTexture, GUILayout.MaxWidth(256), GUILayout.MaxHeight(256), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            else
            {
                GUILayout.Label("No Image Selected");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Generated SDF Preview");
            if (afterSdfTexture != null)
            {
                GUILayout.Label(afterSdfTexture, GUILayout.MaxWidth(256), GUILayout.MaxHeight(256), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            else
            {
                GUILayout.Label("No SDF Generated");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(10);

        TriggerPreprocess(needRefreshRT);

        // �ⲿ����Ԥ�����ڣ�������ʾ���ɵ�SDF��ͼ���Լ�������ɰ�ť
        if (GUILayout.Button("Generate"))
        {
            if (mode == SDFMode.UDF_bin)
            {
                if (afterPreprocessTexture == null)
                    return;
                originalSdfTexture = SDFUtils.GetRenderTextureFromTexture2D(afterPreprocessTexture);
                sdfGenerator.GenerateUDF(ref originalSdfTexture, sdfWidth, sdfHeight, pixelCountToArriveOne);
                afterSdfTexture = SDFUtils.GetTexture2DFromRenderTexture(originalSdfTexture);
                needRefreshRT = true;
            }
            if (mode == SDFMode.SDF_face)
            {
                if (shadowPathList != null && shadowPathList.Count > 1)
                {
                    int sumTime = shadowPathList.Count - 1;
                    List<RenderTexture> sdfList = new List<RenderTexture>();
                    for (int index = 0; index < shadowPathList.Count - 1; index++)
                    {
                        Texture2D shadow0 = new Texture2D(2, 2);
                        shadow0.LoadImage(System.IO.File.ReadAllBytes(shadowPathList[index]));
                        shadow0.Apply();
                        Texture2D shadow1 = new Texture2D(2, 2);
                        shadow1.LoadImage(System.IO.File.ReadAllBytes(shadowPathList[index + 1]));
                        shadow1.Apply();
                        RenderTexture rt0 = sdfGenerator.PreprocessImage(shadow0, channel, 0.5f, false, sdfWidth, sdfHeight, RenderTextureFormat.ARGBFloat);
                        RenderTexture rt1 = sdfGenerator.PreprocessImage(shadow1, channel, 0.5f, true, sdfWidth, sdfHeight, RenderTextureFormat.ARGBFloat);
                        var sdfBetweenTwo = sdfGenerator.GenerateFaceSDFBetweenTwo(ref rt0, ref rt1, sdfWidth, sdfHeight, 0, 0);
                        sdfList.Add(sdfBetweenTwo);
                    }
                    originalSdfTexture = sdfGenerator.SumFaceSDF(ref sdfList);
                    afterSdfTexture = SDFUtils.GetTexture2DFromRenderTexture(originalSdfTexture);
                    needRefreshRT = true;
                }
            }
        }
        if (GUILayout.Button("Save As Exr"))
        {
            if (afterSdfTexture != null)
            {
                string path = EditorUtility.SaveFilePanel("Save SDF", "", "SDF", "exr");
                if (path.Length != 0)
                {
                    Texture2D tex = SDFUtils.GetTexture2DFromRenderTexture(originalSdfTexture);
                    byte[] bytes = tex.EncodeToEXR();
                    System.IO.File.WriteAllBytes(path, bytes);
                }
            }
        }
        if (GUILayout.Button("Save As PNG"))
        {
            if (afterSdfTexture != null)
            {
                string path = EditorUtility.SaveFilePanel("Save SDF", "", "SDF", "png");
                if (path.Length != 0)
                {
                    System.IO.File.WriteAllBytes(path, afterSdfTexture.EncodeToPNG());
                }
            }
        }
        if (GUILayout.Button("Save As TGA"))
        {
            if (afterSdfTexture != null)
            {
                string path = EditorUtility.SaveFilePanel("Save SDF", "", "SDF", "tga");
                if (path.Length != 0)
                {
                    System.IO.File.WriteAllBytes(path, afterSdfTexture.EncodeToTGA());
                }
            }
        }
    }

    private void OnCommonGUI(ref bool needRefreshRT)
    {
        // ���ɵ�SDF��ͼ�ߴ�
        GUILayout.BeginHorizontal();
        var nowWidth = EditorGUILayout.IntField("Width", sdfWidth);
        var nowHeight = EditorGUILayout.IntField("Height", sdfHeight);
        if (nowWidth != sdfWidth || nowHeight != sdfHeight)
        {
            sdfWidth = nowWidth;
            sdfHeight = nowHeight;
            needRefreshRT = true;
        }
        GUILayout.EndHorizontal();
        var newchannel = (SDFChannel)EditorGUILayout.EnumPopup("Channel", channel);
        if (newchannel != channel)
        {
            channel = newchannel;
            needRefreshRT = true;
        }
        var nowStrectch = EditorGUILayout.Toggle("Stretch To Square", strectchToSquare);
        if (nowStrectch != strectchToSquare)
        {
            strectchToSquare = nowStrectch;
            needRefreshRT = true;
        }
        if (strectchToSquare)
        {
            sdfWidth = sdfHeight = Mathf.Max(sdfWidth, sdfHeight);
        }
        if (GUILayout.Button("Fit To Image"))
        {
            if (waitForSDF != null)
            {
                sdfWidth = waitForSDF.width;
                sdfHeight = waitForSDF.height;
                needRefreshRT = true;
            }
        }
        pixelCountToArriveOne = EditorGUILayout.IntField("Pixel Count To Arrive One", pixelCountToArriveOne);
    }

    private void OnBinGUI(ref bool needRefreshRT)
    {
        // ѡ��ͼƬ
        GUILayout.BeginHorizontal();
        // ��������Ҫ��һ����ť�������ֶ�ѡ��Ӳ����ͼƬ
        if (GUILayout.Button("Select Image From Disk", GUILayout.Height(80)))
        {
            // ֧��png��jpg��jpeg��bmp��tga�ȸ�ʽ
            string path = EditorUtility.OpenFilePanel("Select Image", "", "png,jpg,jpeg,bmp,tga");
            if (path.Length != 0)
            {
                waitForSDF = new Texture2D(2, 2);
                waitForSDF.LoadImage(System.IO.File.ReadAllBytes(path));
                waitForSDF.Apply();
                waitForSDF_InProj = null;
                waitForSDF.name = path;
                needRefreshRT = true;
            }
        }
        // ������Ҫһ�����ã�����ѡ����Ŀ�ڵ�Texture2D
        var nowWaitForSDF_InProj = (Texture2D)EditorGUILayout.ObjectField("Select Image From Project", waitForSDF_InProj, typeof(Texture2D), false);
        if (nowWaitForSDF_InProj != waitForSDF_InProj && nowWaitForSDF_InProj != null)
        {
            waitForSDF_InProj = nowWaitForSDF_InProj;
            waitForSDF = nowWaitForSDF_InProj;
            needRefreshRT = true;
        }
        GUILayout.EndHorizontal();
        // ��ֵ����ֵ
        var newThreshold = EditorGUILayout.Slider("Threshold", binThreshold, 0, 1);
        if (newThreshold != binThreshold)
        {
            binThreshold = newThreshold;
            needRefreshRT = true;
        }
        // ���ɲ���
        var nowInvert = EditorGUILayout.Toggle("Invert Channel", binInvert);
        if (nowInvert != binInvert)
        {
            binInvert = nowInvert;
            needRefreshRT = true;
        }
    }

    private void OnHeightGUI()
    {
        // todo
    }

    private void OnFaceGUI(ref bool needRefreshRT)
    {
        // ѡ��ͼƬ
        GUILayout.BeginVertical();
        // ����һ���������ڽ����Ϸ�
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag & Drop Image Here");
        Event evt = Event.current;
        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition))
                    break;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var paths = DragAndDrop.paths;
                    var objs = DragAndDrop.objectReferences;
                    if (objs.Length == 0)
                    {
                        // �Ǵ����������ļ�
                        foreach (var path in paths)
                        {
                            var lower = path.ToLower();
                            if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".bmp") || lower.EndsWith(".tga"))
                            {
                                if (shadowPathList == null)
                                    shadowPathList = new List<string>();
                                shadowPathList.Add(path);
                                needRefreshRT = true;
                                if (shadowTexList != null)
                                    shadowTexList.Clear();
                                shadowTexList = null;
                            }
                        }
                        if (shadowPathList != null)
                        {
                            shadowPathList.Sort();
                        }
                    }
                    else
                    {
                        // �Ǵ���Ŀ��������ļ�
                        foreach (var obj in objs)
                        {
                            var tex = obj as Texture2D;
                            if (tex != null)
                            {
                                if (shadowTexList == null)
                                    shadowTexList = new List<Texture2D>();
                                shadowTexList.Add(tex);
                                needRefreshRT = true;
                                if (shadowPathList != null)
                                    shadowPathList.Clear();
                                shadowPathList = null;
                            }
                        }
                        if (shadowTexList != null)
                        {
                            shadowTexList.Sort((a, b) => a.name.CompareTo(b.name));
                        }
                    }
                }
                break;
        }
        // ��ʾ���е�·��
        if (shadowPathList != null)
        {
            foreach (var path in shadowPathList)
            {
                GUILayout.Label(path);
            }
        }
        // ��ʾ���е�ͼƬ
        if (shadowTexList != null)
        {
            foreach (var tex in shadowTexList)
            {
                GUILayout.Label(tex);
            }
        }
        if (GUILayout.Button("Clear"))
        {
            if (shadowPathList != null)
                shadowPathList.Clear();
            if (shadowTexList != null)
                shadowTexList.Clear();
        }
        GUILayout.EndVertical();

    }

    private void TriggerPreprocess(bool needRefreshRT)
    {
        if (needRefreshRT == false)
            return;
        if (mode == SDFMode.UDF_bin)
        {
            if (waitForSDF != null)
            {
                // Ԥ����ͼƬ
                RenderTexture tempRT = sdfGenerator.PreprocessImage(waitForSDF, channel, binThreshold, binInvert, sdfWidth, sdfHeight);
                afterPreprocessTexture = new Texture2D(sdfWidth, sdfHeight, TextureFormat.RGBA32, false);
                RenderTexture oldRT = RenderTexture.active;
                RenderTexture.active = tempRT;
                afterPreprocessTexture.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                afterPreprocessTexture.Apply();
                RenderTexture.active = oldRT;
                RenderTexture.ReleaseTemporary(tempRT);
                afterPreprocessTexture.name = waitForSDF.name;
            }
        }
        else if (mode == SDFMode.SDF_face)
        {

        }
    }
}
