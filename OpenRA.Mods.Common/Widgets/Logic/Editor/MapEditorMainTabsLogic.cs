#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapEditorMainTabsLogic : ChromeLogic
	{
		readonly Widget widget;
		readonly EditorViewportControllerWidget editor;
		readonly Widget tabContainer;

		protected enum MenuType { Select, Paint, Display, History }
		protected MenuType menuType = MenuType.Paint;

		[ObjectCreator.UseCtor]
		public MapEditorMainTabsLogic(Widget widget)
		{
			this.widget = widget;
			editor = widget.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");
			tabContainer = widget.Get("MAIN_TAB_CONTAINER");

			SetupTab(null, "SELECT_WIDGETS", MenuType.Select);
			SetupTab("PAINT_TAB", "PAINT_WIDGETS", MenuType.Paint);
			SetupTab("TOOLS_TAB", "TOOLS_WIDGETS", MenuType.Display);
			SetupTab("HISTORY_TAB", "HISTORY_WIDGETS", MenuType.History);
		}

		void SetupTab(string buttonId, string tabId, MenuType tabType)
		{
			if (buttonId != null)
			{
				var tab = tabContainer.Get<ButtonWidget>(buttonId);
				tab.IsHighlighted = () => menuType == tabType;
				tab.OnClick = () => menuType = SelectTab(tabType);
			}

			var container = widget.Parent.Get<ContainerWidget>(tabId);
			container.IsVisible = () => menuType == tabType;
		}

		MenuType SelectTab(MenuType newMenuType)
		{
			if (menuType == MenuType.Select)
			{
				editor.SetBrush(editor.DefaultBrush);
				editor.DefaultBrush.ClearSelection();
			}

			return newMenuType;
		}

		public override void Tick()
		{
			var actor = editor.DefaultBrush.Selection.Actor;
			var area = editor.DefaultBrush.Selection.Area;
			if (menuType != MenuType.Select && (actor != null || area != null))
				menuType = MenuType.Select;
			else if (menuType == MenuType.Select && actor == null && area == null)
				menuType = MenuType.Paint;
		}
	}
}
