using DPMLib;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.OleDb;
using System.Data.SqlTypes;
using System.Xml;
using Xunit;
using Xunit.Abstractions;

namespace test
{
  public class TestLoadXmlFromSvr
  {
    private readonly ITestOutputHelper output;
    private FilteredMappings loadedMappings = new FilteredMappings("HSTG");

    public TestLoadXmlFromSvr(ITestOutputHelper output)
    {
      this.output = output;

      // retrieve metadata as xml from target server
      string sqlStatementForTablesToImport = @"
select (
select mappingName = N'm_sync_' + tables.[name],
    mappingDescription = N'HSTG',
    [enabled] = 'true',
    [schemaExt] = (
            select objectSchema = schema_name(tables.[schema_id]),
                objectName = tables.[name]
            for xml path('underlyingSource'), root('AzP_EDW'), type
            ),
    [source] = (
            select dataStore = (
                        select connectionKey = 'HSTG_PRD'
                        for xml path(''), type
                        ),
                [language] = 'T-SQL',
                code = 'select * from ' + schema_name(tables.[schema_id]) + N'.' + tables.[name] + N' where OMD_INSERT_DATETIME &gt; ? AND OMD_INSERT_DATETIME &lt;= ?;'
            for xml path(''), type
            ),
    [target] = (
            select dataStore = (
                        select connectionKey = 'HSTG'
                        for xml path(''), type
                        ),
                objectSchema = schema_name(tables.[schema_id]),
                objectName = tables.[name]
            for xml path(''), type
            ),
    mappedDataItems = (
            select sourceDataItem = (select columnName = columns.[name] for xml path(''), type),
                targetColumn = (select columnName = columns.[name] for xml path(''), type)
            from sys.columns
            where columns.[object_id] = tables.[object_id]
                and columns.[name] not like 'OMD\_%' escape '\'
            order by columns.column_id
            for xml path('mappedDataItem'), type
            )
from EDW_150_History_Area.sys.tables
    /* inner join openquery(AUBIWSQLPRD, '
            select schema_name(tables.[schema_id]) as [schema_name]
                , tables.[name]
            from EDW_150_History_Area.sys.tables;
            ') src
        on src.[schema_name] = schema_name(tables.[schema_id])
        and src.[name] = tables.[name] */
where schema_name(tables.[schema_id]) = N'dbo'
order by schema_name(tables.[schema_id]), tables.[name]
for xml path('mappedDataSets'), root('loaded')
) as xmlColumn;
";

      using (OleDbConnection conn = new OleDbConnection(@"Provider=SQLNCLI11;Server=(local);Integrated Security=SSPI;Initial Catalog=EDW_150_History_Area;"))
      {
        OleDbCommand cmd = new OleDbCommand(sqlStatementForTablesToImport, conn);

        conn.Open();
        OleDbDataReader reader = cmd.ExecuteReader();

        string xmlMetadata = "";
        while (reader.Read())
        {
          // read xml to string (assuming single row)
          xmlMetadata = reader.GetString(0);
        }
        reader.Close();

        // load to object model
        List<MappedDataSet> loaded = XmlLoader.LoadFromString(xmlMetadata);
        loadedMappings.MappedDataSets.AddRange(loaded);
      }
    }

    /* Start of Tests */

    [Fact]
    public void EnsureLoadedSomething()
    {
      if (loadedMappings.MappedDataSets.Count == 0)
        throw new Exception("ERROR: No items in collection.");
    }
  }
}
