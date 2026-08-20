using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Preview renderer for config assets: draws a Sprite, Texture2D or
    /// GameObject (model) centered in a rect, with drag-to-rotate for models
    /// and a context menu hook. Framework port of the source
    /// <c>ConfigWindowPreview</c> (third-party editor dependencies removed).
    /// </summary>
    public class ConfigDataPreview
    {
        private Action<GenericMenu> mMenuController;

        private PreviewRenderUtility mPreview;
        private GameObject mModel;
        private GameObject mModelInstance;
        private Bounds mModelBounds;
        private Vector2 mModelLightEuler = new Vector2(180f, 0);
        private Vector2 mModelCameraEuler = new Vector2(-180f, 0f);

        public ConfigDataPreview(Action<GenericMenu> menuController)
        {
            mMenuController = menuController;
        }

        public void Cleanup()
        {
            if (mModelInstance != null)
                GameObject.DestroyImmediate(mModelInstance);

            if (mPreview != null)
                mPreview.Cleanup();

            mModel = null;
            mPreview = null;
        }

        public void Draw(Rect rect, Sprite sprite)
        {
            Cleanup();

            if (sprite != null)
            {
                var rectW = rect.width;
                var rectH = rect.height;
                var rectAspect = rectW / rectH;

                var spriteW = sprite.textureRect.width;
                var spriteH = sprite.textureRect.height;
                var spriteAspect = spriteW / spriteH;

                var uv0x = sprite.textureRect.xMin * 1.0f / sprite.texture.width;
                var uv0y = sprite.textureRect.yMin * 1.0f / sprite.texture.height;
                var uv1x = sprite.textureRect.xMax * 1.0f / sprite.texture.width;
                var uv1y = sprite.textureRect.yMax * 1.0f / sprite.texture.height;

                var uv = new Rect(uv0x, uv0y, uv1x - uv0x, uv1y - uv0y);

                if (spriteAspect > rectAspect)
                {
                    var drawW = Mathf.Min(spriteW, rectW);
                    var drawH = drawW * (spriteH / spriteW);
                    var drawX = (rectW - drawW) * 0.5f + rect.x;
                    var drawY = (rectH - drawH) * 0.5f + rect.y;
                    GUI.DrawTextureWithTexCoords(new Rect(drawX, drawY, drawW, drawH), sprite.texture, uv);
                }
                else
                {
                    var drawH = Mathf.Min(spriteH, rectH);
                    var drawW = drawH * (spriteW / spriteH);
                    var drawX = (rectW - drawW) * 0.5f + rect.x;
                    var drawY = (rectH - drawH) * 0.5f + rect.y;
                    GUI.DrawTextureWithTexCoords(new Rect(drawX, drawY, drawW, drawH), sprite.texture, uv);
                }
            }

            HandleMenu(rect);
        }

        public void Draw(Rect rect, Texture2D texture)
        {
            Cleanup();

            if (texture != null)
            {
                var rectW = rect.width;
                var rectH = rect.height;
                var rectAspect = rectW / rectH;

                var texW = texture.width - 1.0f;
                var texH = texture.height - 1.0f;
                var texAspect = texW / texH;

                if (texAspect > rectAspect)
                {
                    var drawW = Mathf.Min(texW, rectW);
                    var drawH = drawW * (texH / texW);
                    var drawX = (rectW - drawW) * 0.5f + rect.x;
                    var drawY = (rectH - drawH) * 0.5f + rect.y;
                    GUI.DrawTexture(new Rect(drawX, drawY, drawW, drawH), texture);
                }
                else
                {
                    var drawH = Mathf.Min(texH, rectH);
                    var drawW = drawH * (texW / texH);
                    var drawX = (rectW - drawW) * 0.5f + rect.x;
                    var drawY = (rectH - drawH) * 0.5f + rect.y;
                    GUI.DrawTexture(new Rect(drawX, drawY, drawW, drawH), texture);
                }
            }

            HandleMenu(rect);
        }

        public void Draw(Rect rect, GameObject model)
        {
            if (mModel != model)
            {
                mModel = model;

                if (mModel != null)
                {
                    if (mPreview == null)
                        mPreview = new PreviewRenderUtility();

                    if (mModelInstance != null)
                        GameObject.DestroyImmediate(mModelInstance);

                    mModelInstance = mPreview.InstantiatePrefabInScene(mModel);
                    mModelInstance.transform.position = Vector3.zero;
                    mModelInstance.transform.rotation = Quaternion.identity;
                    mModelInstance.transform.localScale = Vector3.one;

                    AdjustCameraArgs();
                }
                else
                {
                    if (mModelInstance != null)
                        GameObject.DestroyImmediate(mModelInstance);

                    if (mPreview != null)
                        mPreview.Cleanup();

                    mModelInstance = null;
                    mPreview = null;
                }
            }

            if (mPreview == null)
                return;

            mModelCameraEuler = HandleDrag(mModelCameraEuler, rect);

            if (Event.current.type == EventType.Repaint)
            {
                mPreview.BeginPreview(rect, GUIStyle.none);

                var backDistance = 6.0f;
                var fov = 60;
                var modelHeight = Vector3.Distance(mModelBounds.max, mModelBounds.min);
                if (modelHeight > 0)
                    backDistance = (float)((modelHeight * 0.5f) / Math.Tan(fov * 0.5f * Mathf.Deg2Rad));

                var camera = mPreview.camera;
                camera.transform.position = Vector2.zero;
                camera.transform.rotation = Quaternion.Euler(new Vector3(-mModelCameraEuler.y, -mModelCameraEuler.x, 0));
                camera.transform.position = camera.transform.forward * -backDistance;

                EditorUtility.SetCameraAnimateMaterials(camera, true);

                camera.cameraType = CameraType.Preview;
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.Color;
                camera.fieldOfView = fov;
                camera.farClipPlane = 60000.0f;
                camera.nearClipPlane = 0.01f;
                camera.backgroundColor = new Color(49.0f / 255.0f, 77.0f / 255.0f, 121.0f / 255.0f, 0f);

                mPreview.lights[0].intensity = 0.7f;
                mPreview.lights[0].transform.rotation = Quaternion.Euler(mModelLightEuler.x, mModelLightEuler.y, 0f);
                mPreview.lights[1].intensity = 0.7f;
                mPreview.lights[1].transform.rotation = Quaternion.Euler(mModelLightEuler.x, mModelLightEuler.y, 0f);
                mPreview.ambientColor = new Color(0.3f, 0.3f, 0.3f, 0f);

                camera.Render();

                mPreview.EndAndDrawPreview(rect);
            }

            HandleMenu(rect);
        }

        private void AdjustCameraArgs()
        {
            if (mModelInstance == null)
                return;

            mModelBounds = new Bounds();
            var first = true;
            foreach (var bounds in mModelInstance.GetComponentsInChildren<Renderer>().Select(r => r.bounds))
            {
                if (first)
                    mModelBounds = bounds;
                else
                    mModelBounds.Encapsulate(bounds);
                first = false;
            }

            mModelInstance.transform.position = mModelBounds.center * -1;
        }

        private void HandleMenu(Rect position)
        {
            var controlID = GUIUtility.GetControlID("ConfigDataPreview".GetHashCode(), FocusType.Passive);

            var current = Event.current;
            if (current.GetTypeForControl(controlID) == EventType.ContextClick)
            {
                if (position.Contains(current.mousePosition) && position.width > 50f)
                {
                    GUIUtility.hotControl = controlID;
                    current.Use();
                    EditorGUIUtility.SetWantsMouseJumping(1);
                }

                if (mMenuController != null)
                {
                    var menu = new GenericMenu();
                    mMenuController.Invoke(menu);
                    menu.ShowAsContext();
                }
            }
        }

        private Vector2 HandleDrag(Vector2 dragPosition, Rect position)
        {
            var controlID = GUIUtility.GetControlID("ConfigDataPreview".GetHashCode(), FocusType.Passive);

            var current = Event.current;
            switch (current.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (position.Contains(current.mousePosition) && position.width > 50f)
                    {
                        GUIUtility.hotControl = controlID;
                        current.Use();
                        EditorGUIUtility.SetWantsMouseJumping(1);
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                        GUIUtility.hotControl = 0;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        dragPosition -= current.delta * (float)(current.shift ? 3 : 1) / Mathf.Min(position.width, position.height) * 140f;
                        current.Use();
                        GUI.changed = true;
                    }
                    break;
            }

            return dragPosition;
        }
    }
}
