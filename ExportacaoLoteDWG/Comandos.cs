using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ExportacaoLoteDWG.Comandos))]

namespace ExportacaoLoteDWG
{
    public class Comandos
    {
        // Comando digitado no AutoCAD: EXPORTLOTE
        // CommandFlags.Session é obrigatório para abrir/fechar outros DWGs.
        [CommandMethod("EXPORTLOTE", CommandFlags.Session)]
        public async void ExportarLote()
        {
            Document docOriginal = AcApp.DocumentManager.MdiActiveDocument;
            Editor edOriginal = docOriginal.Editor;

            // 1) Selecionar os DWGs
            string[] arquivos;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Selecione os DWGs exportados do Revit";
                ofd.Filter = "Desenhos AutoCAD (*.dwg)|*.dwg";
                ofd.Multiselect = true;
                if (ofd.ShowDialog() != DialogResult.OK || ofd.FileNames.Length == 0)
                {
                    edOriginal.WriteMessage("\nOperação cancelada.");
                    return;
                }
                arquivos = ofd.FileNames;
            }

            // 2) Selecionar a pasta de destino
            string pastaDestino;
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Selecione a pasta onde os novos DWGs serão salvos";
                fbd.ShowNewFolderButton = true;
                if (fbd.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    edOriginal.WriteMessage("\nOperação cancelada.");
                    return;
                }
                pastaDestino = fbd.SelectedPath;
            }

            // Guarda variáveis de sistema para restaurar depois
            object filediaOriginal = AcApp.GetSystemVariable("FILEDIA");
            object cmdechoOriginal = AcApp.GetSystemVariable("CMDECHO");

            var log = new StringBuilder();
            int ok = 0, falhas = 0;

            try
            {
                // FILEDIA=0 faz o EXPORTLAYOUT pedir o nome no prompt de comando
                // (sem abrir janela), permitindo automatizar.
                AcApp.SetSystemVariable("FILEDIA", 0);
                AcApp.SetSystemVariable("CMDECHO", 0);

                int i = 0;
                foreach (string origem in arquivos)
                {
                    i++;
                    string nome = Path.GetFileName(origem);
                    edOriginal.WriteMessage($"\n[{i}/{arquivos.Length}] Processando {nome} ...");

                    Document doc = null;
                    string etapa = "iniciar";
                    try
                    {
                        // Define o caminho de saída
                        string destino = Path.Combine(pastaDestino, nome);
                        if (string.Equals(Path.GetFullPath(destino), Path.GetFullPath(origem),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            // Nunca sobrescrever o arquivo de origem
                            destino = Path.Combine(pastaDestino,
                                Path.GetFileNameWithoutExtension(nome) + "_Model.dwg");
                        }

                        // Evita o prompt "arquivo já existe, substituir?"
                        if (File.Exists(destino))
                            File.Delete(destino);

                        // Abre o DWG (se já estiver aberto, reutiliza)
                        etapa = "abrir DWG";
                        doc = BuscarDocumentoAberto(origem);
                        bool jaEstavaAberto = doc != null;
                        if (doc == null)
                            doc = AcApp.DocumentManager.Open(origem, false);

                        etapa = "ativar DWG";
                        AcApp.DocumentManager.MdiActiveDocument = doc;
                        etapa = "trocar layout";

                        // Vai para o Layout1 (ou, se não existir, o primeiro layout de papel)
                        etapa = "localizar layout";
                        string layout = ObterNomeLayout(doc);
                        if (layout == null)
                            throw new System.Exception("Nenhum layout encontrado no desenho.");

                        // A troca do documento ativo só se completa depois que o comando
                        // cede a vez; por isso o layout é trocado via comando executado
                        // na fila do próprio documento (e não via LayoutManager).
                        // Os comandos rodam dentro do contexto de comando do documento já
                        // ativo (ExecuteInCommandContextAsync espera a ativação terminar).
                        string layoutFinal = layout;
                        string destinoFinal = destino;
                        Document docFinal = doc;
                        string etapaInterna = "trocar layout";
                        try
                        {
                            await AcApp.DocumentManager.ExecuteInCommandContextAsync(
                                async (obj) =>
                                {
                                    etapaInterna = "trocar layout";
                                    await docFinal.Editor.CommandAsync("_.LAYOUT", "_Set", layoutFinal);

                                    etapaInterna = "EXPORTLAYOUT";
                                    await docFinal.Editor.CommandAsync("_.EXPORTLAYOUT", destinoFinal);
                                },
                                null);
                        }
                        catch
                        {
                            etapa = etapaInterna;
                            throw;
                        }

                        // O EXPORTLAYOUT pode abrir o DWG novo; fecha se isso ocorrer
                        etapa = "fechar DWG exportado";
                        FecharDocumentoSeAberto(destino);

                        if (File.Exists(destino))
                        {
                            ok++;
                            log.AppendLine($"OK     | {nome} -> {destino} (layout: {layout})");
                        }
                        else
                        {
                            falhas++;
                            log.AppendLine($"FALHA  | {nome} | arquivo de saída não foi criado");
                        }

                        // Fecha o DWG de origem sem salvar (não foi alterado de fato)
                        if (!jaEstavaAberto)
                        {
                            AcApp.DocumentManager.MdiActiveDocument = docOriginal;
                            doc.CloseAndDiscard();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        falhas++;
                        log.AppendLine($"FALHA  | {nome} | etapa: {etapa} | {ex.Message}");
                        try
                        {
                            if (doc != null && doc != docOriginal)
                            {
                                AcApp.DocumentManager.MdiActiveDocument = docOriginal;
                                doc.CloseAndDiscard();
                            }
                        }
                        catch { /* ignora erro ao fechar */ }
                    }
                }
            }
            finally
            {
                // Restaura configurações
                AcApp.SetSystemVariable("FILEDIA", filediaOriginal);
                AcApp.SetSystemVariable("CMDECHO", cmdechoOriginal);

                try { AcApp.DocumentManager.MdiActiveDocument = docOriginal; } catch { }

                // Grava log na pasta de destino
                string caminhoLog = Path.Combine(pastaDestino, "ExportacaoLote_log.txt");
                try { File.WriteAllText(caminhoLog, log.ToString(), Encoding.UTF8); } catch { }

                edOriginal.WriteMessage($"\nConcluído: {ok} exportado(s), {falhas} falha(s).");
                edOriginal.WriteMessage($"\nLog: {caminhoLog}\n");
            }
        }

        private static Document BuscarDocumentoAberto(string caminho)
        {
            foreach (Document d in AcApp.DocumentManager)
            {
                if (string.Equals(d.Name, caminho, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return null;
        }

        private static void FecharDocumentoSeAberto(string caminho)
        {
            var d = BuscarDocumentoAberto(caminho);
            if (d != null)
            {
                try { d.CloseAndDiscard(); } catch { }
            }
        }

        // Retorna "Layout1" se existir; senão o primeiro layout que não seja Model
        private static string ObterNomeLayout(Document doc)
        {
            string primeiro = null;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
                var lista = new List<Layout>();
                foreach (DBDictionaryEntry entry in dict)
                {
                    var lay = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    if (string.Equals(lay.LayoutName, "Layout1", StringComparison.OrdinalIgnoreCase))
                        return lay.LayoutName;
                    lista.Add(lay);
                }
                if (lista.Count > 0)
                {
                    lista.Sort((a, b) => a.TabOrder.CompareTo(b.TabOrder));
                    primeiro = lista[0].LayoutName;
                }
                tr.Commit();
            }
            return primeiro;
        }
    }
}