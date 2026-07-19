using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class ReadingAssignmentTests
{
    [Fact]
    public void BookDisplayOption_Defaults_NotRead()
    {
        var opt = new BookDisplayOption
        {
            BookId = "b1",
            Title = "测试书",
            RequiredHours = 8,
        };
        Assert.Equal("b1", opt.BookId);
        Assert.Equal("测试书", opt.Title);
        Assert.False(opt.IsRead);
        Assert.Equal(0.0, opt.ReadHours);
        Assert.Equal(8.0, opt.RequiredHours);
        Assert.Null(opt.PrerequisiteBookId);
        Assert.Null(opt.PrerequisiteTitle);
    }

    [Fact]
    public void BookDisplayOption_ReadState()
    {
        var opt = new BookDisplayOption
        {
            BookId = "book_a",
            Title = "已读书",
            IsRead = true,
            ReadHours = 8,
            RequiredHours = 8,
        };
        Assert.True(opt.IsRead);
        Assert.Equal(8.0, opt.ReadHours);
        Assert.Equal(8.0, opt.RequiredHours);
    }

    [Fact]
    public void BookDisplayOption_PartialProgress()
    {
        var opt = new BookDisplayOption
        {
            BookId = "book_b",
            Title = "读了一半",
            ReadHours = 4.0,
            RequiredHours = 8.0,
        };
        Assert.False(opt.IsRead);
        Assert.Equal(4.0, opt.ReadHours);
        Assert.Equal(8.0, opt.RequiredHours);
    }

    [Fact]
    public void BookDisplayOption_ZeroProgress()
    {
        var opt = new BookDisplayOption
        {
            BookId = "book_c",
            Title = "未读",
            ReadHours = 0.0,
            RequiredHours = 6.0,
        };
        Assert.False(opt.IsRead);
        Assert.Equal(0.0, opt.ReadHours);
        Assert.Equal(6.0, opt.RequiredHours);
    }

    [Fact]
    public void BookDisplayOption_WithPrerequisite()
    {
        var opt = new BookDisplayOption
        {
            BookId = "advanced",
            Title = "进阶书",
            PrerequisiteBookId = "basics",
            PrerequisiteTitle = "入门书",
        };
        Assert.Equal("basics", opt.PrerequisiteBookId);
        Assert.Equal("入门书", opt.PrerequisiteTitle);
    }

    [Fact]
    public void ReaderOption_Defaults()
    {
        var r = new ReaderOption { Id = 42, Name = "张三" };
        Assert.Equal(42, r.Id);
        Assert.Equal("张三", r.Name);
    }

    [Fact]
    public void ReadingAssignmentView_Properties()
    {
        var pawns = new List<ReaderOption>
        {
            new() { Id = 1, Name = "张三" },
            new() { Id = 2, Name = "李四" },
        };
        var books = new List<BookDisplayOption>
        {
            new() { BookId = "b1", Title = "书一", RequiredHours = 8 },
            new() { BookId = "b2", Title = "书二", RequiredHours = 6, IsRead = true, ReadHours = 6 },
        };
        bool? callbackCalled = null;
        Func<int, string, bool>? hasRead = (id, bookId) =>
        {
            callbackCalled = true;
            return id == 1 && bookId == "b1";
        };

        var view = new ReadingAssignmentView
        {
            Pawns = pawns,
            Books = books,
            ReaderHasReadBook = hasRead,
        };

        Assert.Same(pawns, view.Pawns);
        Assert.Same(books, view.Books);
        Assert.NotNull(view.ReaderHasReadBook);

        Assert.True(view.ReaderHasReadBook(1, "b1"));
        Assert.True(callbackCalled!.Value);
        Assert.False(view.ReaderHasReadBook(2, "b1"));
    }

    [Fact]
    public void ReadingAssignmentView_NullHasRead_DefaultsToFalse()
    {
        var view = new ReadingAssignmentView
        {
            Pawns = Array.Empty<ReaderOption>(),
            Books = Array.Empty<BookDisplayOption>(),
            ReaderHasReadBook = null,
        };
        Assert.Empty(view.Pawns);
        Assert.Empty(view.Books);
        Assert.Null(view.ReaderHasReadBook);
    }

    [Fact]
    public void ReadingAssignmentView_MultipleBooks_DifferentReadStates()
    {
        var books = new List<BookDisplayOption>
        {
            new() { BookId = "a", Title = "已读", IsRead = true, ReadHours = 4.0, RequiredHours = 4.0 },
            new() { BookId = "b", Title = "半本", IsRead = false, ReadHours = 2.0, RequiredHours = 4.0 },
            new() { BookId = "c", Title = "没动", IsRead = false, ReadHours = 0.0, RequiredHours = 6.0 },
        };

        Assert.Equal(2, books.Count(b => !b.IsRead));
        Assert.Single(books.Where(b => b.IsRead));
        Assert.Equal(2.0, books[1].ReadHours);
        Assert.Equal(6.0, books[2].RequiredHours);
    }

    [Fact]
    public void ReaderOption_IdenticalIds_AreDistinct()
    {
        var pawns = new[]
        {
            new ReaderOption { Id = 1, Name = "张三" },
            new ReaderOption { Id = 1, Name = "张三" },
        };
        Assert.Equal(2, pawns.Length);
        Assert.Equal(pawns[0].Id, pawns[1].Id);
    }
}
