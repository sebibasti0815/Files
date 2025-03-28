﻿// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Actions
{
	internal sealed class NewTabAction : IAction
	{
		public string Label
			=> Strings.NewTab.GetLocalizedResource();

		public string Description
			=> Strings.NewTabDescription.GetLocalizedResource();

		public HotKey HotKey
			=> new(Keys.T, KeyModifiers.Ctrl);

		public NewTabAction()
		{
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			return NavigationHelpers.AddNewTabAsync();
		}
	}
}
