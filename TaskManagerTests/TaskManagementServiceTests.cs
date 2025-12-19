using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManagementServices.Model;
using TaskManagementServices.Model.Interfaces;
using TaskManagementServices.Services;

namespace TaskManagerTests
{
    public class TaskManagementServiceTests
    {
        private readonly Mock<ITaskRepository> _repo;
        private readonly Mock<IUserService> _users;
        private readonly Mock<INotificationService> _notif;
        private readonly Mock<IAuditService> _audit;

        private readonly TaskManagementService _service;

        public TaskManagementServiceTests()
        {
            _repo = new Mock<ITaskRepository>();
            _users = new Mock<IUserService>();
            _notif = new Mock<INotificationService>();
            _audit = new Mock<IAuditService>();

            _service = new TaskManagementService(
                _repo.Object, _users.Object, _notif.Object, _audit.Object);
        }

        [Fact]
        public void Requirement01_ShouldThrowException_WhenUserIsInactive()
        {
            _users.Setup(u => u.IsActiveUser(10)).Returns(false);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _service.CreateTask(10, "Test", "Low", null));
            Assert.Equal("Inactive user.", ex.Message);
        }

        [Fact]
        public void Requirement02_ShouldThrowException_WhenTitleIsEmpty()
        {
            _users.Setup(u => u.IsActiveUser(1)).Returns(true);

            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateTask(1, "", "Low", null));
            Assert.Equal("Title required.", ex.Message);
        }

        [Fact]
        public void Requirement03_ShouldThrowException_WhenDeadlineIsInPast()
        {
            _users.Setup(u => u.IsActiveUser(1)).Returns(true);
            var pastDate = DateTime.UtcNow.AddDays(-1);

            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateTask(1, "Test", "Low", pastDate));
            Assert.Equal("Deadline cannot be in the past.", ex.Message);
        }

        [Fact]
        public void Requirement04_ShouldThrowException_WhenPriorityIsInvalid()
        {
            _users.Setup(u => u.IsActiveUser(1)).Returns(true);

            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateTask(1, "Test", "Urgent", null));
            Assert.Equal("Invalid priority.", ex.Message);
        }

        [Fact]
        public void Requirement05_ShouldSaveNotifyAndLog_WhenCreated()
        {
            _users.Setup(u => u.IsActiveUser(1)).Returns(true);

            var result = _service.CreateTask(1, "Valid Task", "High", null);

            Assert.NotNull(result);
            _repo.Verify(r => r.Save(It.IsAny<TaskItem>()), Times.Once);
            _notif.Verify(n => n.NotifyCreated(1, It.IsAny<string>()), Times.Once);
            _audit.Verify(a => a.Log("CREATE", 1, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void Requirement06_ShouldIncrementId_WhenTasksCreated()
        {
            _users.Setup(u => u.IsActiveUser(It.IsAny<int>())).Returns(true);

            int idCounter = 1;
            _repo.Setup(r => r.Save(It.IsAny<TaskItem>()))
                 .Callback<TaskItem>(t => t.Id = idCounter++);

            var task1 = _service.CreateTask(1, "Task 1", "Low", null);
            var task2 = _service.CreateTask(1, "Task 2", "Low", null);

            Assert.Equal(1, task1.Id);
            Assert.Equal(2, task2.Id);
            Assert.Equal(task1.Id + 1, task2.Id);
        }

        [Fact]
        public void Requirement07_ShouldReturnFalse_WhenCompletingInvalidTask()
        {
            _repo.Setup(r => r.FindTask(999)).Returns((TaskItem)null);

            var result = _service.CompleteTask(1, 999);

            Assert.False(result);
        }

        [Fact]
        public void Requirement08_ShouldReturnFalse_WhenAlreadyCompleted()
        {
            var task = new TaskItem { Id = 5, UserId = 1, IsCompleted = true };
            _repo.Setup(r => r.FindTask(5)).Returns(task);

            var result = _service.CompleteTask(1, 5);

            Assert.False(result);
        }

        [Fact]
        public void Requirement09_ShouldCompleteSuccessfully()
        {
            var task = new TaskItem { Id = 10, UserId = 1, IsCompleted = false, Title = "Task 10" };
            _repo.Setup(r => r.FindTask(10)).Returns(task);

            var result = _service.CompleteTask(1, 10);

            Assert.True(result);
            Assert.True(task.IsCompleted);
            _repo.Verify(r => r.Save(task), Times.Once);
            _notif.Verify(n => n.NotifyCompleted(1, "Task 10"), Times.Once);
            _audit.Verify(a => a.Log("COMPLETE", 1, "Task 10"), Times.Once);
        }

        [Fact]
        public void Requirement10_ShouldReturnFalse_WhenDeletingInvalidTask()
        {
            var task = new TaskItem { Id = 20, UserId = 2 };
            _repo.Setup(r => r.FindTask(20)).Returns(task);

            var result = _service.DeleteTask(1, 20);

            Assert.False(result);
            _repo.Verify(r => r.Delete(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Requirement11_ShouldDeleteAndLog()
        {
            var task = new TaskItem { Id = 20, UserId = 1, Title = "Task 20" };
            _repo.Setup(r => r.FindTask(20)).Returns(task);

            var result = _service.DeleteTask(1, 20);

            Assert.True(result);
            _repo.Verify(r => r.Delete(20), Times.Once);
            _audit.Verify(a => a.Log("DELETE", 1, "Task 20"), Times.Once);
        }

        [Fact]
        public void Requirement12_ShouldReturnOnlyActiveTasks()
        {
            var tasks = new List<TaskItem>
            {
                new TaskItem { Id = 1, IsCompleted = false },
                new TaskItem { Id = 2, IsCompleted = true }
            };
            _repo.Setup(r => r.GetUserTasks(1)).Returns(tasks);

            var result = _service.GetActiveTasks(1);

            Assert.Single(result);
            Assert.Equal(1, result.First().Id);
        }

        [Fact]
        public void Requirement13_ShouldReturnOnlyOverdueActiveTasks()
        {
            var tasks = new List<TaskItem>
            {
                new TaskItem { Id = 1, IsCompleted = false, Deadline = DateTime.UtcNow.AddDays(-1) },
                new TaskItem { Id = 2, IsCompleted = false, Deadline = DateTime.UtcNow.AddDays(1) },
                new TaskItem { Id = 3, IsCompleted = true, Deadline = DateTime.UtcNow.AddDays(-1) }
            };
            _repo.Setup(r => r.GetUserTasks(1)).Returns(tasks);

            var result = _service.GetOverdueTasks(1);

            Assert.Single(result);
            Assert.Equal(1, result.First().Id);
        }

        [Fact]
        public void Requirement14_ShouldReturnFalse_WhenUpdatingOthersTask()
        {
            var task = new TaskItem { Id = 30, UserId = 2 }; 
            _repo.Setup(r => r.FindTask(30)).Returns(task);

            var result = _service.UpdatePriority(1, 30, "High");

            Assert.False(result);
        }

        [Fact]
        public void Requirement15_ShouldReturnFalse_WhenUpdatingWithInvalidPriority()
        {
            var task = new TaskItem { Id = 30, UserId = 1 };
            _repo.Setup(r => r.FindTask(30)).Returns(task);

            var result = _service.UpdatePriority(1, 30, "SuperCritical");

            Assert.False(result);
            _repo.Verify(r => r.Save(It.IsAny<TaskItem>()), Times.Never);
        }
    }
}
