using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// "Full Explode" sólo existe como comando postable de la interfaz de Revit
    /// (no hay método de API que lo ejecute de forma síncrona). Esta clase
    /// selecciona cada ImportInstance de la cola y despacha el comando con
    /// <see cref="UIApplication.PostCommand"/>, avanzando entre ciclos del
    /// evento Idling hasta que Revit termina de procesar cada explosión.
    /// </summary>
    internal class ExplodeQueueProcessor
    {
        private const int MaxIdleTicksPerElement = 200;

        private readonly UIApplication _uiApp;
        private readonly Queue<ElementId> _pending;
        private readonly int _unpinnedCount;
        private readonly int _totalCount;

        private ElementId _currentId;
        private bool _commandPosted;
        private int _idleTicksWaited;
        private int _explodedCount;
        private readonly List<ElementId> _failedIds = new List<ElementId>();

        private ExplodeQueueProcessor(UIApplication uiApp, IEnumerable<ElementId> ids, int unpinnedCount)
        {
            _uiApp = uiApp;
            _pending = new Queue<ElementId>(ids);
            _unpinnedCount = unpinnedCount;
            _totalCount = _pending.Count;
        }

        public static void Start(UIApplication uiApp, IEnumerable<ElementId> ids, int unpinnedCount)
        {
            var processor = new ExplodeQueueProcessor(uiApp, ids, unpinnedCount);
            uiApp.Idling += processor.OnIdling;
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                Finish();
                return;
            }

            if (_currentId != null)
            {
                // Mientras el elemento siga existiendo, Revit todavía no ha
                // terminado (o no ha empezado) a procesar el Full Explode.
                Element stillThere = doc.GetElement(_currentId);
                if (stillThere == null)
                {
                    // El ImportInstance desapareció: fue reemplazado por los
                    // elementos nativos resultantes del explode.
                    _explodedCount++;
                    _currentId = null;
                }
                else if (_commandPosted)
                {
                    _idleTicksWaited++;
                    if (_idleTicksWaited > MaxIdleTicksPerElement)
                    {
                        // Se agotó la espera (por ejemplo, el usuario canceló el
                        // diálogo de Revit o el elemento no se puede explotar).
                        _failedIds.Add(_currentId);
                        _currentId = null;
                    }
                    else
                    {
                        e.SetRaiseWithoutDelay();
                        return;
                    }
                }
            }

            if (_currentId == null)
            {
                if (_pending.Count == 0)
                {
                    Finish();
                    return;
                }

                _currentId = _pending.Dequeue();
                _commandPosted = false;
                _idleTicksWaited = 0;
            }

            if (!_commandPosted)
            {
                UIDocument uiDoc = _uiApp.ActiveUIDocument;
                uiDoc.Selection.SetElementIds(new List<ElementId> { _currentId });

                RevitCommandId fullExplodeId =
                    RevitCommandId.LookupPostableCommandId(PostableCommand.FullExplode);

                if (fullExplodeId != null && _uiApp.CanPostCommand(fullExplodeId))
                {
                    _uiApp.PostCommand(fullExplodeId);
                    _commandPosted = true;
                }
                else
                {
                    _failedIds.Add(_currentId);
                    _currentId = null;
                }
            }

            e.SetRaiseWithoutDelay();
        }

        private void Finish()
        {
            _uiApp.Idling -= OnIdling;

            string summary =
                $"DWGs procesados: {_totalCount}\n" +
                $"Explotados correctamente: {_explodedCount}\n" +
                $"No se pudieron explotar: {_failedIds.Count}\n" +
                (_unpinnedCount > 0
                    ? $"\nSe desancló (Unpin) automáticamente {_unpinnedCount} elemento(s) " +
                      "para poder explotarlos."
                    : string.Empty);

            TaskDialog.Show("Explotar DWGs — resumen", summary);
        }
    }
}
