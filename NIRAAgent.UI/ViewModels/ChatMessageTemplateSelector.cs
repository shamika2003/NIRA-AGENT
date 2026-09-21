/*
 * filename: ChatMessageTemplateSelector.cs
 */


using System.Windows;
using System.Windows.Controls;

namespace NIRAAgent.UI.ViewModels;

public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }

    public DataTemplate? AssistantTemplate { get; set; }

    public override DataTemplate? SelectTemplate(
        object item,
        DependencyObject container)
    {
        if (item is ChatMessageViewModel message)
        {
            if (message.IsUser)
                return UserTemplate;

            if (message.IsAssistant)
                return AssistantTemplate;
        }

        return base.SelectTemplate(item, container);
    }
}
