// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

using Microsoft.Toolkit.Uwp.UI.Controls.Design.Common;
using Microsoft.Windows.Design;
using Microsoft.Windows.Design.Metadata;

namespace Microsoft.Toolkit.Uwp.UI.Controls.Design
{
    internal class CustomDialogMetadata : AttributeTableBuilder
    {
        public CustomDialogMetadata()
            : base()
        {
            AddCallback(typeof(AdaptiveGridView),
                b =>
                {
                    b.AddCustomAttributes(nameof(AdaptiveGridView.DesiredWidth),
                        new CategoryAttribute(Properties.Resources.CategoryCommon)
                        );
                    b.AddCustomAttributes(nameof(AdaptiveGridView.ItemHeight),
                        new CategoryAttribute(Properties.Resources.CategoryCommon)
                        );
                    b.AddCustomAttributes(nameof(AdaptiveGridView.OneRowModeEnabled),
                       new CategoryAttribute(Properties.Resources.CategoryCommon)
                       );
                    b.AddCustomAttributes(nameof(AdaptiveGridView.StretchContentForSingleRow),
                        new CategoryAttribute(Properties.Resources.CategoryCommon)
                        );
                    b.AddCustomAttributes(nameof(AdaptiveGridView.ItemClickCommand),
                        new EditorBrowsableAttribute(EditorBrowsableState.Advanced),
                        new CategoryAttribute(Properties.Resources.CategoryCommon)
                        );

                    b.AddCustomAttributes(new ToolboxCategoryAttribute(ToolboxCategoryPaths.Toolkit, false));
                }
            );
        }
    }
}