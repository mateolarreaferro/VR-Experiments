﻿/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using UnityEditor;
using UnityEngine;

namespace Meta.Voice.Hub.UIComponents
{
    public class ImageView
    {
        private EditorWindow _editorWindow;
        private Editor _editor;
        private Vector2 pan;
        private float zoom = -1f;

        public ImageView(EditorWindow editorWindow) => _editorWindow = editorWindow;
        public ImageView(Editor editor) => _editor = editor;

        private float ViewHeight => _editorWindow ? _editorWindow.position.height : Screen.height;
        private float ViewWidth => _editorWindow ? _editorWindow.position.width : EditorGUIUtility.currentViewWidth;

        private void Repaint()
        {
            if (_editorWindow) _editorWindow.Repaint();
            else if (_editor) _editor.Repaint();
        }

        public void Draw(Texture2D image)
        {
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            var windowRect = GUILayoutUtility.GetLastRect();
            if (windowRect.width <= 1 && windowRect.height <= 1) return;

            if (image == null)
            {
                EditorGUILayout.HelpBox("No Texture2D assigned.", MessageType.Info);
                return;
            }

            // Handle input for panning and zooming
            HandleInput();

            GUI.BeginGroup(windowRect);

            var imageWidth = image.width * zoom;
            var imageHeight = image.height * zoom;

            if (zoom < 0 || imageWidth < windowRect.width && imageHeight < windowRect.height)
            {
                float widthScale = windowRect.width / image.width;
                float heightScale = windowRect.height / image.height;
                zoom = Mathf.Min(widthScale, heightScale);
            }

            if (imageWidth < windowRect.width) pan.x = (windowRect.width - imageWidth) / 2.0f;
            else if (pan.x + imageWidth < windowRect.width) pan.x += windowRect.width - (pan.x + imageWidth);

            if (imageHeight < windowRect.height) pan.y = (windowRect.height - imageHeight) / 2.0f;
            else if (pan.y + imageHeight < windowRect.height) pan.y += windowRect.height - (pan.y + imageHeight);

            if (pan.x > 0) pan.x = 0;
            if (pan.y > 0) pan.y = 0;

            if (imageHeight < windowRect.height) pan.y = (windowRect.height - imageHeight) / 2.0f;

            GUI.DrawTexture(new Rect(pan.x, pan.y, image.width * zoom, image.height * zoom), image, ScaleMode.ScaleAndCrop);

            GUI.EndGroup();
        }

        private void HandleInput()
        {
            Event e = Event.current;

            // Panning
            if (e.type == EventType.MouseDown)
            {
                e.Use();
            }

            if (e.type == EventType.MouseDrag)
            {
                pan += e.delta;
                e.Use();
            }

            // Zooming
            if (e.type == EventType.ScrollWheel)
            {
                float zoomDelta = -e.delta.y * 0.01f;
                zoom = Mathf.Clamp(zoom + zoomDelta, 0.1f, 10f);
                e.Use();
            }
        }
    }
}
