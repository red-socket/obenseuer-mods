using System;
using OS.Items;

namespace FairMarket
{
    // What an invoice letter really is (spec §9.1), read only from the game's record: InvoiceController.GetInvoice(id).
    // The letter's own nocrime flag is never read, because Item.CloneMeta drops it on every drag (IL-checked). A letter
    // whose record is gone (paid, expired, a blank, a dragged reminder copy with id 0) can't be paid at the machine
    // (SRC/InvoicePaymentMachine.cs:23-27). A vanilla reminder letter keeps its meta nested in an object[], so no invoice
    // code reads it and it gets no marker until a drag turns it into a blank.
    internal enum Mark { None, NoDebt, Scam, ScamExpired }

    internal static class Marker
    {
        internal static Mark For(ItemInvoiceData d)
        {
            if (d == null) return Mark.None;
            try
            {
                var ic = InvoiceController.instance;
                if (ic == null || TimeOfDayAzure.instance == null) return Mark.None;
                var rec = ic.GetInvoice(d.invoiceID);
                if (rec == null) return Mark.NoDebt;
                if (rec.nocrime) return rec.IsExpired() ? Mark.ScamExpired : Mark.Scam;
                return Mark.None;
            }
            catch (Exception e)
            {
                Log.Exception("invoice marker", e);
                return Mark.None;
            }
        }
    }
}
