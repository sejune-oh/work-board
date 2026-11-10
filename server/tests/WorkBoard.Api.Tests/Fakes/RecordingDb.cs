using System.Collections;
using System.Data;
using System.Data.Common;
using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Tests.Fakes;

public sealed record RecordedCommand(string Sql, IReadOnlyDictionary<string, object?> Parameters);

public sealed class RecordingConnectionFactory : IAppConnectionFactory
{
    public List<string> Events { get; } = [];
    public List<RecordedCommand> Commands { get; } = [];

    public DbConnection Create() => new RecordingConnection(this);
}

internal sealed class RecordingConnection(RecordingConnectionFactory log) : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public RecordingConnectionFactory Log => log;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "fake";
    public override string DataSource => "fake";
    public override string ServerVersion => "0";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;

    public override void Open()
    {
        _state = ConnectionState.Open;
        log.Events.Add("open");
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        log.Events.Add("begin");
        return new RecordingTransaction(this, isolationLevel);
    }

    protected override DbCommand CreateDbCommand() => new RecordingCommand(this);
}

internal sealed class RecordingTransaction(RecordingConnection connection, IsolationLevel level) : DbTransaction
{
    public override IsolationLevel IsolationLevel => level;
    protected override DbConnection DbConnection => connection;
    public override void Commit() => connection.Log.Events.Add("commit");
    public override void Rollback() => connection.Log.Events.Add("rollback");
}

internal sealed class RecordingCommand(RecordingConnection connection) : DbCommand
{
    private readonly RecordingParameters _parameters = new();

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get => connection; set { } }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new RecordingParameter();

    public override int ExecuteNonQuery()
    {
        Record();
        return 0;
    }

    public override object? ExecuteScalar()
    {
        Record();
        return null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Record();
        return new DataTable().CreateDataReader();
    }

    private void Record()
    {
        var parameters = _parameters.Items.ToDictionary(p => p.ParameterName.TrimStart('@'), p => p.Value);
        connection.Log.Commands.Add(new RecordedCommand(CommandText, parameters));
        connection.Log.Events.Add("command");
    }
}

internal sealed class RecordingParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = "";
    public override int Size { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}

internal sealed class RecordingParameters : DbParameterCollection
{
    public List<DbParameter> Items { get; } = [];

    public override int Count => Items.Count;
    public override object SyncRoot => ((ICollection)Items).SyncRoot;

    public override int Add(object value)
    {
        Items.Add((DbParameter)value);
        return Items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value);
    }

    public override void Clear() => Items.Clear();
    public override bool Contains(object value) => Items.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => Items.GetEnumerator();
    public override int IndexOf(object value) => Items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => Items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => Items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => Items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => Items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => Items.RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => Items[index];
    protected override DbParameter GetParameter(string parameterName) => Items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => Items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => Items[IndexOf(parameterName)] = value;
}
