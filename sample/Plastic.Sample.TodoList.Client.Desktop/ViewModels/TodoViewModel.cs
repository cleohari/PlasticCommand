﻿namespace Plastic.Sample.TodoList.Client.Desktop.ViewModels
{
    using GalaSoft.MvvmLight;
    using Plastic.Sample.TodoList.AppCommands;

    public class TodoViewModel : ViewModelBase
    {
        private readonly DoneCommand _doneCommand;
        private readonly TodoAgainCommand _todoAgaineCommand;
        private readonly int _id;
        private bool _done;

        public string Title { get; }

        public bool Done
        {
            get => this._done;
            set => SetDone(value);
        }

        public TodoViewModel(
            int id, string title, bool done,
            DoneCommand doneCommand,
            TodoAgainCommand todoAgaineCommand)
        {
            this._id = id;
            this.Title = title;
            this._done = done;
            this._doneCommand = doneCommand;
            this._todoAgaineCommand = todoAgaineCommand;
        }

        protected bool SetDone(bool newState)
        {
            // HACK: It's sample, Don't use like this...
            bool executed;
            if (newState)
            {
                ExecutionResult result = this._doneCommand.ExecuteAsync(this._id).Result;
                executed = result.HasSucceeded();
            }
            else
            {
                ExecutionResult result = this._todoAgaineCommand.ExecuteAsync(this._id).Result;
                executed = result.HasSucceeded();
            }

            return executed ? Set(ref this._done, newState) : false;
        }
    }
}
