using GestureSign.Common.Input;
using System;
using System.Collections.Generic;

namespace GestureSign.Daemon.Triggers
{
    internal interface ITouchpadContactFilter
    {
        IReadOnlyList<TouchpadContact> Filter(IReadOnlyList<TouchpadContact> contacts);
    }

    internal sealed class TouchpadConfidenceContactFilter : ITouchpadContactFilter
    {
        public IReadOnlyList<TouchpadContact> Filter(IReadOnlyList<TouchpadContact> contacts)
        {
            if (contacts == null)
                throw new ArgumentNullException(nameof(contacts));

            List<TouchpadContact> filtered = null;
            for (int i = 0; i < contacts.Count; i++)
            {
                TouchpadContact contact = contacts[i];
                bool exclude = contact.IsActive && contact.IsLowConfidence;
                if (exclude)
                {
                    if (filtered == null)
                    {
                        filtered = new List<TouchpadContact>(contacts.Count - 1);
                        for (int precedingIndex = 0; precedingIndex < i; precedingIndex++)
                            filtered.Add(contacts[precedingIndex]);
                    }
                    continue;
                }

                filtered?.Add(contact);
            }

            return filtered ?? contacts;
        }
    }
}
