using Il2CppInterop.Runtime;
using Il2CppSimpleColorPicker.Scripts;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Valve.VR;
using Valve.VR.InteractionSystem;
using Object = UnityEngine.Object;

namespace HPVR.UI
{
    internal static class UIManager
    {

        private static readonly HashSet<Transform> canvasses = new();
        private static readonly HashSet<MonoBehaviour> UIElements = new();
        public static bool UpdateUIPos = true;
        public static List<Transform> CanvasToIgnore = new();
        private static bool initialized = false;
        static public void Initialize()
        {
            if (!initialized)
            {
                Player.instance.leftHand.OnHandInitialized += i => { Player.instance.leftHand.gameObject.AddComponent<Laser>(); };
                Player.instance.rightHand.OnHandInitialized += i => { Player.instance.rightHand.gameObject.AddComponent<Laser>(); };
                initialized = true;
            }
        }

        public static void Update()
        {
            UpdateUIPositions();
        }

        private static void UpdateUIPositions()
        {
            if (!UpdateUIPos)
            {
                foreach (var canvas in canvasses)
                {
                    canvas.position = new(1, 1.4f, -8.3f);
                }
            }
            else
            {
                foreach (var canvas in canvasses)
                {
                    if (!canvas.gameObject.active)
                    {
                        continue;
                    }

                    if (CanvasToIgnore.Contains(canvas))
                    {
                        continue;
                    }

                    //todo move some UI differently than others, for example move bgc text field more down, thought bubble more left and so on. Need to make a map for that
                    canvas.position = SteamVR_Camera.instance.transform.position + (SteamVR_Camera.instance.transform.rotation * Vector3.forward * 1.4f);
                    canvas.rotation = SteamVR_Camera.instance.transform.rotation;
                }
            }
        }

        public static void OnSceneChange()
        {
            canvasses.Clear();
            UIElements.Clear();
            //only move ui which is notr already world space
            //set scale to 0.001 for all axis
            //set about 1.7 units in front of the vr cam
            foreach (var gameObject in GameObject.FindObjectsOfTypeAll(Il2CppType.Of<Canvas>()))
            {
                var canvas = gameObject.TryCast<Canvas>();
                if (canvas is null || gameObject.hideFlags != HideFlags.None)
                {
                    //MelonLogger.Msg(gameObject.name + " " + ((int)gameObject.hideFlags));
                    continue;
                }
                if (canvasses.Contains(canvas.transform))
                {
                    continue;
                }

                MelonLogger.Msg(canvas.name + " original Position and scale" + canvas.transform.position.ToString() + " - " + canvas.transform.localScale.ToString());

                if (canvas.transform.localScale == Vector3.zero)
                {
                    canvas.transform.localScale = Vector3.one;
                }

                switch (canvas.renderMode)
                {
                    case RenderMode.WorldSpace:
                        continue;
                    case RenderMode.ScreenSpaceCamera:
                        canvas.transform.localScale *= 0.02f;
                        break;
                    default:
                        canvas.transform.localScale *= 0.0008f;
                        break;
                }

                //todo tune
                //canvas.scaleFactor *= 1.1f;
                canvas.renderMode = RenderMode.WorldSpace;
                canvasses.Add(canvas.transform);
            }
            UpdateUIPositions();

            //todo sliders are found and they trigger to 0 on click, have to investigate their original unity classes
            //todo the dropdowns drop down, but the colliders behind still trigger. also the dropdowns collider is not correctly adjusted to the new size
            //todo the scrollviews block the rest with their colliders, only enable those which would be visible
            //see simplecolorpicker 
            //also in the customization environment things are offset

            SetUpUIObjectsOfType<Selectable>();
            SetUpUIObjectsOfType<Dropdown.DropdownItem>();
            SetUpUIObjectsOfType<TMP_Dropdown.DropdownItem>();
            SetUpUIObjectsOfType<PaletteGradient>();
        }

        //void SimpleColorPicker.Scripts.PaletteGradient$$OnPointerDown
        //                (SimpleColorPicker_Scripts_PaletteGradient_o* __this,
        //                UnityEngine_EventSystems_PointerEventData_o* eventData, MethodInfo* method)
        //{
        //    code* pcVar1;
        //    UnityEngine_Vector2_Fields screenPoint;
        //    bool isInRectangle;
        //    int32_t canvasrenderMode;
        //    UnityEngine_RectTransform_o* rect;
        //    UnityEngine_Camera_o* cam;
        //    UnityEngine_Vector2_Fields position;
        //    UnityEngine_Canvas_o* canvas;
        //    SimpleColorPicker_Scripts_ColorJoystick_o* colorJoystick;

        //    position.x = 0.0;
        //    position.y = 0.0;
        //    rect = UnityEngine.Component.GetComponent<RectTransform>());
        //    if (eventData is not null)
        //    {
        //        canvas = (__this->fields).Canvas;
        //        screenPoint = (eventData->fields)._position_k__BackingField.fields;
        //        if (canvas is not null)
        //        {
        //            canvasrenderMode = UnityEngine.Canvas$$get_renderMode(canvas, (MethodInfo*)0x0);
        //            if (canvasrenderMode == 1)
        //            {
        //                cam = UnityEngine.Camera$$get_main((MethodInfo*)0x0);
        //            }
        //            else
        //            {
        //                cam = (UnityEngine_Camera_o*)0x0;
        //            }
        //            
        //            isInRectangle =
        //                    UnityEngine.RectTransformUtility$$ScreenPointToLocalPointInRectangle
        //                            (rect, (UnityEngine_Vector2_o)screenPoint, cam, (UnityEngine_Vector2_o*)&position
        //                            , (MethodInfo*)0x0);
        //            if (isInRectangle)
        //            {
        //                colorJoystick = (__this->fields).ColorJoystick;
        //                if (colorJoystick is null)
        //                    goto LAB_1806cc252;
        //                SimpleColorPicker.Scripts.ColorJoystick$$OnDrag(colorJoystick, eventData, (MethodInfo*)0x0);
        //            }
        //            return;
        //        }
        //    }
        //LAB_1806cc252:
        //    FUN_180396d50();
        //    pcVar1 = (code*)swi(3);
        //    (*pcVar1)();
        //    return;
        //}

        //void SimpleColorPicker.Scripts.PaletteGradient$$OnDrag
        //                (SimpleColorPicker_Scripts_PaletteGradient_o* __this,
        //                UnityEngine_EventSystems_PointerEventData_o* eventData, MethodInfo* method)
        //{
        //    code* pcVar1;
        //    SimpleColorPicker_Scripts_ColorJoystick_o* colorJoystick;

        //    colorJoystick = (__this->fields).ColorJoystick;
        //    if (colorJoystick is not null)
        //    {
        //        SimpleColorPicker.Scripts.ColorJoystick$$OnDrag(colorJoystick, eventData, (MethodInfo*)0x0);
        //        return;
        //    }
        //    FUN_180396d50();
        //    pcVar1 = (code*)swi(3);
        //    (*pcVar1)();
        //    return;
        //}

        //void SimpleColorPicker.Scripts.ColorJoystick$$OnDrag

        //                (SimpleColorPicker_Scripts_ColorJoystick_o* __this,
        //                UnityEngine_EventSystems_PointerEventData_o* eventData, MethodInfo* method)

        //{
        //    code* pcVar1;
        //    UnityEngine_Vector2_Fields screenPoint;
        //    undefined8 uVar2;
        //    undefined8 uVar3;
        //    bool inRectPlane;
        //    int32_t canvasRenderMode;
        //    int iVar4;
        //    int iVar5;
        //    int iVar6;
        //    int iVar7;
        //    UnityEngine_Camera_o* cam;
        //    UnityEngine_Rect_o* Rect;
        //    UnityEngine_Transform_o* transform;
        //    UnityEngine_Color_o* color;
        //    float H;
        //    float SumMin;
        //    UnityEngine_Vector2_o eventPosition;
        //    UnityEngine_Rect_o Newrect[6];
        //    UnityEngine_Canvas_o* Canvas;
        //    SimpleColorPicker_Scripts_ColorPicker_o* ColorPickerScript;
        //    SimpleColorPicker_Scripts_ColorSlider_o* ColorSliderScript;
        //    UnityEngine_UI_Slider_o* ColorSliderSlider;
        //    UnityEngine_Texture2D_o* Tex2D;
        //    float eventX;
        //    float floatVal;
        //    UnityEngine_RectTransform_o* rectTransform;
        //    float rectheight;

        //    if (DAT_183a97ecd == '\0')
        //    {
        //        thunk_FUN_1803888b0(&UnityEngine.RectTransformUtility_TypeInfo);
        //        DAT_183a97ecd = '\x01';
        //    }
        //    rectTransform = (__this->fields).RectTransform;
        //    eventPosition.fields.x = 0.0;
        //    eventPosition.fields.y = 0.0;
        //    if (eventData != (UnityEngine_EventSystems_PointerEventData_o*)0x0)
        //    {
        //        Canvas = (__this->fields).Canvas;
        //        screenPoint = (eventData->fields)._position_k__BackingField.fields;
        //        if (Canvas != (UnityEngine_Canvas_o*)0x0)
        //        {
        //            canvasRenderMode = UnityEngine.Canvas$$get_renderMode(Canvas, (MethodInfo*)0x0);
        //            if (canvasRenderMode == 1)
        //            {
        //                cam = UnityEngine.Camera$$get_main((MethodInfo*)0x0);
        //            }
        //            else
        //            {
        //                cam = (UnityEngine_Camera_o*)0x0;
        //            }
        //            if ((UnityEngine.RectTransformUtility_TypeInfo->_2).cctor_finished == 0)
        //            {
        //                il2cpp_runtime_class_init();
        //            }
        //            inRectPlane = UnityEngine.RectTransformUtility$$ScreenPointToLocalPointInRectangle
        //                                    (rectTransform, (UnityEngine_Vector2_o)screenPoint, cam, &eventPosition,
        //                                        (MethodInfo*)0x0);
        //            floatVal = eventPosition.fields.x;
        //            if (!inRectPlane)
        //            {
        //                return;
        //            }
        //            rectTransform = (__this->fields).RectTransform;
        //            if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //            {
        //                Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                SumMin = eventPosition.fields.y;
        //                eventX = (Rect->fields).m_XMin;
        //                eventPosition.fields.x = floatVal;
        //                if (floatVal <= eventX)
        //                {
        //                    eventPosition.fields.x = eventX;
        //                }
        //                rectTransform = (__this->fields).RectTransform;
        //                if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                {
        //                    Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                    eventX = eventPosition.fields.x;
        //                    floatVal = (Rect->fields).m_YMin;
        //                    eventPosition.fields.y = SumMin;
        //                    if (SumMin <= floatVal)
        //                    {
        //                        eventPosition.fields.y = floatVal;
        //                    }
        //                    rectTransform = (__this->fields).RectTransform;
        //                    if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                    {
        //                        Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                        floatVal = eventPosition.fields.y;
        //                        SumMin = (Rect->fields).m_Width + (Rect->fields).m_XMin;
        //                        eventPosition.fields.x = eventX;
        //                        if (SumMin <= eventX)
        //                        {
        //                            eventPosition.fields.x = SumMin;
        //                        }
        //                        rectTransform = (__this->fields).RectTransform;
        //                        if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                        {
        //                            Rect = UnityEngine.RectTransform$$get_rect(Newrect, rectTransform, (MethodInfo*)0x0);
        //                            eventX = (Rect->fields).m_Height + (Rect->fields).m_YMin;
        //                            eventPosition.fields.y = floatVal;
        //                            if (eventX <= floatVal)
        //                            {
        //                                eventPosition.fields.y = eventX;
        //                            }
        //                            transform = UnityEngine.Component$$get_transform
        //                                                    ((UnityEngine_Component_o*)__this, (MethodInfo*)0x0);
        //                            if (transform != (UnityEngine_Transform_o*)0x0)
        //                            {
        //                                Newrect[0].fields.m_YMin = eventPosition.fields.y;
        //                                Newrect[0].fields.m_XMin = eventPosition.fields.x;
        //                                Newrect[0].fields.m_Width = 0.0;
        //                                UnityEngine.Transform$$set_localPosition
        //                                            (transform, (UnityEngine_Vector3_o*)Newrect, (MethodInfo*)0x0);
        //                                floatVal = eventPosition.fields.x;
        //                                ColorPickerScript = (__this->fields).ColorPicker;
        //                                if (ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                {
        //                                    rectTransform = (__this->fields).RectTransform;
        //                                    Tex2D = (ColorPickerScript->fields).Texture;
        //                                    if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                                    {
        //                                        Rect = UnityEngine.RectTransform$$get_rect
        //                                                            (Newrect, rectTransform, (MethodInfo*)0x0);
        //                                        eventX = (Rect->fields).m_Width;
        //                                        if (Tex2D != (UnityEngine_Texture2D_o*)0x0)
        //                                        {
        //                                            iVar4 = (*(Tex2D->klass->vtable)._5_get_width.methodPtr)
        //                                                                (Tex2D, (Tex2D->klass->vtable)._5_get_width.method);
        //                                            SumMin = eventPosition.fields.y;
        //                                            rectTransform = (__this->fields).RectTransform;
        //                                            if (rectTransform != (UnityEngine_RectTransform_o*)0x0)
        //                                            {
        //                                                Rect = UnityEngine.RectTransform$$get_rect
        //                                                                    (Newrect, rectTransform, (MethodInfo*)0x0);
        //                                                rectheight = (Rect->fields).m_Height;
        //                                                iVar5 = (*(Tex2D->klass->vtable)._7_get_height.methodPtr)
        //                                                                    (Tex2D, (Tex2D->klass->vtable)._7_get_height.method);
        //                                                ColorPickerScript = (__this->fields).ColorPicker;
        //                                                if (((ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                                    && (ColorSliderScript = (ColorPickerScript->fields).H,
        //                                                        ColorSliderScript != (SimpleColorPicker_Scripts_ColorSlider_o*)0x0))
        //                                                    && (ColorSliderSlider = (ColorSliderScript->fields).Slider,
        //                                                        ColorSliderSlider != (UnityEngine_UI_Slider_o*)0x0))
        //                                                {
        //                                                    H = (float)(*(ColorSliderSlider->klass->vtable)._46_get_value.methodPtr)
        //                                                                            (ColorSliderSlider,
        //                                                                            (ColorSliderSlider->klass->vtable)._46_get_value.
        //                                                                            method);
        //                                                    iVar6 = (*(Tex2D->klass->vtable)._5_get_width.methodPtr)
        //                                                                        (Tex2D, (Tex2D->klass->vtable)._5_get_width.method);
        //                                                    iVar7 = (*(Tex2D->klass->vtable)._7_get_height.methodPtr)
        //                                                                        (Tex2D, (Tex2D->klass->vtable)._7_get_height.method);
        //                                                    color = UnityEngine.Color$$HSVToRGB
        //                                                                        ((UnityEngine_Color_o*)Newrect, H,
        //                                                                        ((floatVal / eventX) * (float)iVar4) / (float)iVar6,
        //                                                                        ((SumMin / rectheight) * (float)iVar5) / (float)iVar7,
        //                                                                        true, (MethodInfo*)0x0);
        //                                                    uVar3._0_4_ = (color->fields).r;
        //                                                    uVar3._4_4_ = (color->fields).g;
        //                                                    uVar2._0_4_ = (color->fields).r;
        //                                                    uVar2._4_4_ = (color->fields).g;
        //                                                    floatVal = (color->fields).b;
        //                                                    ColorPickerScript = (__this->fields).ColorPicker;
        //                                                    if (((ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                                        && (ColorSliderScript = (ColorPickerScript->fields).A,
        //                                                            ColorSliderScript != (SimpleColorPicker_Scripts_ColorSlider_o*)0x0
        //                                                            )) && (ColorSliderSlider = (ColorSliderScript->fields).Slider,
        //                                                                    ColorSliderSlider != (UnityEngine_UI_Slider_o*)0x0))
        //                                                    {
        //                                                        Newrect[0].fields.m_Height =
        //                                                                (float)(*(ColorSliderSlider->klass->vtable)._46_get_value.methodPtr
        //                                                                    )(ColorSliderSlider,
        //                                                                        (ColorSliderSlider->klass->vtable)._46_get_value.method);
        //                                                        ColorPickerScript = (__this->fields).ColorPicker;
        //                                                        Newrect[0].fields._0_8_ = uVar2;
        //                                                        Newrect[0].fields.m_Width = floatVal;
        //                                                        if (ColorPickerScript != (SimpleColorPicker_Scripts_ColorPicker_o*)0x0)
        //                                                        {
        //                                                            Newrect[0].fields._0_8_ = uVar3;
        //                                                            SimpleColorPicker.Scripts.ColorPicker$$SetColor
        //                                                                        (ColorPickerScript, (UnityEngine_Color_o*)Newrect, false, true
        //                                                                        , true, true, (MethodInfo*)0x0);
        //                                                            return;
        //                                                        }
        //                                                    }
        //                                                }
        //                                            }
        //                                        }
        //                                    }
        //                                }
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    FUN_180396d50();
        //    pcVar1 = (code*)swi(3);
        //    (*pcVar1)();
        //    return;
        //}

        //todo for the color picker it seems we just want to call the colorjoysticks ondrag ourselfs
        //if the position is right it should be fine, seems we send the wrong coordinates
        //we already have to translate the hit coords into the local points for the object beforehand

        private static void SetUpUIObjectsOfType<T>() where T : MonoBehaviour
        {
            foreach (var obj in Object.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
            {
                var uiComponent = obj.TryCast<T>();
                if (uiComponent?.gameObject?.hideFlags != HideFlags.None)
                { continue; }

                if (UIElements.Contains(uiComponent))
                { continue; }

                UIElements.Add(uiComponent);

                var inter = uiComponent.gameObject.AddComponent<Interactable>();
                inter.highlightOnHover = false;
                inter.handFollowTransform = false;
                inter.snapAttachEaseInTime = 0.15f;
                inter.useHandObjectAttachmentPoint = false;

                uiComponent.gameObject.AddComponent<UIElement>();
            }
        }

    }
}
