#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	public class EditorMoveActorHotkeyLogic : ChromeLogic
	{
		EditorViewportControllerWidget editor;

		[ObjectCreator.UseCtor]
		public EditorMoveActorHotkeyLogic(Widget widget, ModData modData, Dictionary<string, MiniYaml> logicArgs)
		{
			var keyhandler = widget.Get<LogicKeyListenerWidget>("GLOBAL_KEYHANDLER");
			keyhandler.AddHandler(e =>
			{
				// Hotkeys do not support only a modifier key; actor move override key will have to be hardcoded for now
				if (e.Key != Keycode.LSHIFT)
					return false;

				if (editor == null)
				{
					var worldRoot = Ui.Root.Get<ContainerWidget>("EDITOR_WORLD_ROOT");
					editor = worldRoot.Get<EditorViewportControllerWidget>("MAP_EDITOR");
				}

				editor.DefaultBrush.ActorFreeMove = e.Event == KeyInputEvent.Down;
				return false;
			});
		}
	}
}
