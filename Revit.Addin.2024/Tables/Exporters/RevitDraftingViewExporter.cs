using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TableData = Revit.Addin._2024.Tables.Models.TableData;
using TableRow = Revit.Addin._2024.Tables.Models.TableRow;

namespace Revit.Addin._2024.Tables.Exporters
{
    /// <summary>
    /// Generates the table directly inside the Revit document by creating a new drafting view
    /// and laying out TextNotes + DetailCurves in a grid. Coordinates are in feet (Revit
    /// internal); column widths declared on the template are interpreted as millimetres.
    ///
    /// One transaction wraps the whole creation so a failure leaves no partial view behind.
    /// </summary>
    public class RevitDraftingViewExporter : ITableExporter
    {
        private const double MmPerFoot = 304.8;
        private const double RowHeightMm = 8.0;
        private const double HeaderHeightMm = 9.0;
        private const double SectionHeightMm = 9.0;
        private const double TitleHeightMm = 11.0;
        private const double CellPaddingMm = 1.0;

        public string FormatName => "Revit Drafting View";

        public bool RequiresOutputPath => false;

        public string FileExtension => null;

        public string FileFilter => null;

        public ExportResult Export(TableData data, ExportContext context)
        {
            if (data == null) return Failure("Keine Daten zum Exportieren.");
            if (context?.Document == null) return Failure("Kein aktives Revit-Dokument.");

            var doc = context.Document;

            var draftingViewType = GetDraftingViewFamilyType(doc);
            if (draftingViewType == null)
                return Failure("Keine Plansichts-Familie (Drafting View) im Dokument gefunden.");

            var textType = GetDefaultTextNoteType(doc);
            if (textType == null)
                return Failure("Keine TextNote-Familie im Dokument gefunden.");

            ElementId createdViewId = null;
            string createdViewName = null;

            using (var tx = new Transaction(doc, $"Tabelle erstellen: {data.Title}"))
            {
                tx.Start();
                try
                {
                    var view = ViewDrafting.Create(doc, draftingViewType.Id);
                    createdViewName = MakeUniqueViewName(doc, data.Title);
                    view.Name = createdViewName;
                    createdViewId = view.Id;

                    DrawTable(doc, view, data, textType.Id);

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return Failure($"Erstellung der Plansicht fehlgeschlagen: {ex.Message}");
                }
            }

            // Switch to the new view so the user sees the result.
            try
            {
                if (context.UIDocument != null && createdViewId != null)
                    context.UIDocument.ActiveView = (View)doc.GetElement(createdViewId);
            }
            catch { /* non-fatal */ }

            return new ExportResult
            {
                Success = true,
                Message = $"Plansicht '{createdViewName}' erstellt.",
                ProducedPath = createdViewName,
            };
        }

        private static void DrawTable(Document doc, ViewDrafting view, TableData data, ElementId textTypeId)
        {
            int colCount = data.Columns.Count;
            if (colCount == 0) return;

            // Column X-left coordinates in mm.
            var colLeftMm = new double[colCount];
            double runningMm = 0.0;
            for (int c = 0; c < colCount; c++)
            {
                colLeftMm[c] = runningMm;
                runningMm += data.Columns[c].Width;
            }
            double totalWidthMm = runningMm;

            // Build row plan: each row has a kind (Title/Header/Section/Data), a *minimum*
            // height, and source. Data rows may grow beyond the minimum to fit wrapped text.
            var plan = new List<RowPlan>();
            plan.Add(new RowPlan(RowKind.Title, TitleHeightMm, data.Title, null));
            plan.Add(new RowPlan(RowKind.Header, HeaderHeightMm, null, null));
            foreach (var section in data.Sections)
            {
                plan.Add(new RowPlan(RowKind.Section, SectionHeightMm, section.Name, null));
                foreach (var row in section.Rows)
                    plan.Add(new RowPlan(RowKind.Data, RowHeightMm, null, row));
            }

            // -----------------------------------------------------------------
            // Pass 1 — create each data row's cell texts at a provisional Y. The
            // column widths are fixed, so Revit wraps long text and computes a
            // TextNote.Height we can read back to size the row.
            // -----------------------------------------------------------------
            var provTopMm = new double[plan.Count];
            double provY = 0.0;
            for (int i = 0; i < plan.Count; i++)
            {
                provTopMm[i] = provY;
                provY -= plan[i].HeightMm;
            }

            var dataNotes = new Dictionary<int, List<TextNote>>();
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Kind != RowKind.Data) continue;

                var rp = plan[i];
                var notes = new List<TextNote>();
                for (int c = 0; c < colCount; c++)
                {
                    string cellText = string.Empty;
                    if (rp.Row != null)
                        rp.Row.Cells.TryGetValue(data.Columns[c].Header, out cellText);

                    var note = PlaceCellText(doc, view, textTypeId,
                        colLeftMm[c], colLeftMm[c] + data.Columns[c].Width,
                        provTopMm[i], provTopMm[i] - rp.HeightMm,
                        cellText ?? string.Empty, bold: false, centered: false);
                    if (note != null) notes.Add(note);
                }
                dataNotes[i] = notes;
            }

            // Force Revit to compute the wrapped TextNote heights before we read them.
            doc.Regenerate();

            // -----------------------------------------------------------------
            // Compute final per-row heights: data rows grow to their tallest cell
            // (plus padding); header/section/title keep their fixed heights.
            // -----------------------------------------------------------------
            var rowHeightMm = new double[plan.Count];
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Kind == RowKind.Data)
                {
                    double tallestMm = 0.0;
                    if (dataNotes.TryGetValue(i, out var notes))
                    {
                        foreach (var n in notes)
                            tallestMm = Math.Max(tallestMm, n.Height * MmPerFoot);
                    }
                    rowHeightMm[i] = Math.Max(RowHeightMm, tallestMm + 2 * CellPaddingMm);
                }
                else
                {
                    rowHeightMm[i] = plan[i].HeightMm;
                }
            }

            // Final row tops from the final heights (Y goes downward / negative).
            var rowTopMm = new double[plan.Count];
            double yMm = 0.0;
            for (int i = 0; i < plan.Count; i++)
            {
                rowTopMm[i] = yMm;
                yMm -= rowHeightMm[i];
            }
            double tableBottomMm = yMm;

            // Move the already-created data notes from their provisional Y to the final Y
            // (X is unchanged, so only the vertical coordinate needs updating).
            for (int i = 0; i < plan.Count; i++)
            {
                if (!dataNotes.TryGetValue(i, out var notes)) continue;
                double targetYMm = rowTopMm[i] - CellPaddingMm;
                foreach (var n in notes)
                {
                    XYZ c = n.Coord;
                    n.Coord = new XYZ(c.X, targetYMm / MmPerFoot, c.Z);
                }
            }

            // Three distinct grey shades so the visual hierarchy reads at a glance:
            // table title (darkest) → column headers (medium) → group headers (lightest).
            // Backgrounds go down first so text + grid lines render above.
            ElementId titleFillId   = GetOrCreateGreyFilledRegionType(doc, "Tabelle Titel Grau",  new Color(160, 160, 160));
            ElementId headerFillId  = GetOrCreateGreyFilledRegionType(doc, "Tabelle Header Grau", new Color(200, 200, 200));
            ElementId sectionFillId = GetOrCreateGreyFilledRegionType(doc, "Tabelle Gruppe Grau", new Color(230, 230, 230));

            for (int i = 0; i < plan.Count; i++)
            {
                ElementId fillId;
                switch (plan[i].Kind)
                {
                    case RowKind.Title:   fillId = titleFillId;   break;
                    case RowKind.Header:  fillId = headerFillId;  break;
                    case RowKind.Section: fillId = sectionFillId; break;
                    default: continue;
                }
                if (fillId == ElementId.InvalidElementId) continue;

                double top = rowTopMm[i];
                CreateRectangularFilledRegion(doc, view, fillId, 0, top, totalWidthMm, top - rowHeightMm[i]);
            }

            // Place text for the non-data rows (data text was created + repositioned above).
            for (int i = 0; i < plan.Count; i++)
            {
                var rp = plan[i];
                double top = rowTopMm[i];
                double bottom = top - rowHeightMm[i];

                switch (rp.Kind)
                {
                    case RowKind.Title:
                        PlaceCellText(doc, view, textTypeId, 0, totalWidthMm, top, bottom,
                            rp.Text ?? string.Empty, bold: true, centered: true);
                        break;
                    case RowKind.Header:
                        for (int c = 0; c < colCount; c++)
                        {
                            PlaceCellText(doc, view, textTypeId,
                                colLeftMm[c],
                                colLeftMm[c] + data.Columns[c].Width,
                                top, bottom,
                                data.Columns[c].Header ?? string.Empty,
                                bold: true, centered: true);
                        }
                        break;
                    case RowKind.Section:
                        PlaceCellText(doc, view, textTypeId, 0, totalWidthMm, top, bottom,
                            rp.Text ?? string.Empty, bold: true, centered: false);
                        break;
                }
            }

            // Draw grid lines.
            DrawGrid(doc, view, plan, rowTopMm, rowHeightMm, tableBottomMm, colLeftMm, totalWidthMm);
        }

        private static void DrawGrid(Document doc, ViewDrafting view, List<RowPlan> plan,
            double[] rowTopMm, double[] rowHeightMm, double tableBottomMm, double[] colLeftMm, double totalWidthMm)
        {
            // Horizontal lines: top of every row + final bottom.
            for (int i = 0; i < plan.Count; i++)
                DrawLine(doc, view, 0, rowTopMm[i], totalWidthMm, rowTopMm[i]);
            DrawLine(doc, view, 0, tableBottomMm, totalWidthMm, tableBottomMm);

            // Vertical lines: only on rows that show columns (Header + Data).
            int colCount = colLeftMm.Length;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Kind != RowKind.Header && plan[i].Kind != RowKind.Data) continue;
                double top = rowTopMm[i];
                double bottom = top - rowHeightMm[i];
                for (int c = 0; c <= colCount; c++)
                {
                    double x = c == colCount ? totalWidthMm : colLeftMm[c];
                    DrawLine(doc, view, x, top, x, bottom);
                }
            }

            // Outer left/right borders along the full table height.
            DrawLine(doc, view, 0, 0, 0, tableBottomMm);
            DrawLine(doc, view, totalWidthMm, 0, totalWidthMm, tableBottomMm);
        }

        /// <summary>
        /// Creates a wrapping TextNote inside the given cell box and returns it (or null when
        /// the text is empty). The returned note's <see cref="TextNote.Height"/> reflects the
        /// wrapped line count, which the caller uses to size data rows.
        /// </summary>
        private static TextNote PlaceCellText(Document doc, ViewDrafting view, ElementId textTypeId,
            double xLeftMm, double xRightMm, double yTopMm, double yBottomMm,
            string text, bool bold, bool centered)
        {
            if (string.IsNullOrEmpty(text)) return null;

            double cellWidthMm = xRightMm - xLeftMm;
            double widthMm = Math.Max(1.0, cellWidthMm - 2 * CellPaddingMm);

            // Revit anchors the TextNote based on HorizontalAlignment:
            //   Left   → position = top-left of the bounding box
            //   Center → position = top-center of the bounding box
            // Passing the cell's left edge while asking for centered text shifted headers
            // half a column to the right; correct anchor for center is the cell's mid-X.
            double xMm = centered ? (xLeftMm + cellWidthMm / 2.0) : (xLeftMm + CellPaddingMm);
            double yMm = yTopMm - CellPaddingMm;

            var position = new XYZ(xMm / MmPerFoot, yMm / MmPerFoot, 0);
            double widthFt = widthMm / MmPerFoot;

            var options = new TextNoteOptions(textTypeId)
            {
                HorizontalAlignment = centered ? HorizontalTextAlignment.Center : HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Top,
                KeepRotatedTextReadable = true,
            };

            var note = TextNote.Create(doc, view.Id, position, widthFt, text, options);

            if (bold)
            {
                var fmt = note.GetFormattedText();
                fmt.SetBoldStatus(true);
                note.SetFormattedText(fmt);
            }

            return note;
        }

        private static void DrawLine(Document doc, ViewDrafting view,
            double x1Mm, double y1Mm, double x2Mm, double y2Mm)
        {
            var p1 = new XYZ(x1Mm / MmPerFoot, y1Mm / MmPerFoot, 0);
            var p2 = new XYZ(x2Mm / MmPerFoot, y2Mm / MmPerFoot, 0);
            if (p1.IsAlmostEqualTo(p2)) return;

            var line = Line.CreateBound(p1, p2);
            doc.Create.NewDetailCurve(view, line);
        }

        /// <summary>
        /// Returns a FilledRegionType named <paramref name="typeName"/> configured with a solid
        /// fill of <paramref name="color"/>, creating it on first use by duplicating an existing
        /// FilledRegionType. Returns InvalidElementId only if the document has no
        /// FilledRegionType at all to clone — callers must tolerate this and skip the fill.
        /// </summary>
        private static ElementId GetOrCreateGreyFilledRegionType(Document doc, string typeName, Color color)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(f => string.Equals(f.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;

            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();
            if (baseType == null) return ElementId.InvalidElementId;

            try
            {
                var newType = baseType.Duplicate(typeName) as FilledRegionType;
                if (newType == null) return baseType.Id;

                var solidPattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern() != null && f.GetFillPattern().IsSolidFill);

                if (solidPattern != null)
                {
                    newType.ForegroundPatternId = solidPattern.Id;
                    newType.BackgroundPatternId = ElementId.InvalidElementId;
                }
                newType.ForegroundPatternColor = color;
                newType.IsMasking = false;
                return newType.Id;
            }
            catch
            {
                // If we cannot configure the duplicate, fall back to whatever the project has.
                return baseType.Id;
            }
        }

        private static void CreateRectangularFilledRegion(Document doc, ViewDrafting view,
            ElementId typeId, double xLeftMm, double yTopMm, double xRightMm, double yBottomMm)
        {
            if (typeId == ElementId.InvalidElementId) return;
            if (Math.Abs(xRightMm - xLeftMm) < 1e-6) return;
            if (Math.Abs(yTopMm - yBottomMm) < 1e-6) return;

            var topLeft     = new XYZ(xLeftMm  / MmPerFoot, yTopMm    / MmPerFoot, 0);
            var topRight    = new XYZ(xRightMm / MmPerFoot, yTopMm    / MmPerFoot, 0);
            var bottomRight = new XYZ(xRightMm / MmPerFoot, yBottomMm / MmPerFoot, 0);
            var bottomLeft  = new XYZ(xLeftMm  / MmPerFoot, yBottomMm / MmPerFoot, 0);

            try
            {
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(topLeft, bottomLeft));
                loop.Append(Line.CreateBound(bottomLeft, bottomRight));
                loop.Append(Line.CreateBound(bottomRight, topRight));
                loop.Append(Line.CreateBound(topRight, topLeft));

                FilledRegion.Create(doc, typeId, view.Id, new List<CurveLoop> { loop });
            }
            catch
            {
                // Filled region is decoration only — never block the table over a fill failure.
            }
        }

        private static ViewFamilyType GetDraftingViewFamilyType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
        }

        private static TextNoteType GetDefaultTextNoteType(Document doc)
        {
            // Prefer the document's default text note type if any, else first found.
            var defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (defaultId != null && defaultId != ElementId.InvalidElementId)
            {
                if (doc.GetElement(defaultId) is TextNoteType t) return t;
            }
            return new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();
        }

        private static string MakeUniqueViewName(Document doc, string baseTitle)
        {
            string root = string.IsNullOrWhiteSpace(baseTitle) ? "Tabelle" : baseTitle;
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(root)) return root;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{root} ({i})";
                if (!existing.Contains(candidate)) return candidate;
            }
            return $"{root} ({Guid.NewGuid().ToString("N").Substring(0, 6)})";
        }

        private static ExportResult Failure(string msg) =>
            new ExportResult { Success = false, Message = msg };

        private enum RowKind { Title, Header, Section, Data }

        private sealed class RowPlan
        {
            public RowKind Kind { get; }
            public double HeightMm { get; }
            public string Text { get; }
            public TableRow Row { get; }

            public RowPlan(RowKind kind, double heightMm, string text, TableRow row)
            {
                Kind = kind;
                HeightMm = heightMm;
                Text = text;
                Row = row;
            }
        }
    }
}
