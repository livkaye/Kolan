using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neo4jClient;
using Neo4jClient.Cypher;
using Kolan.Models;
using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("Kolan.Tests")]
namespace Kolan.Repositories
{
    public class BoardRepository : Repository<Board>
    {
        private Generator _generator;

        public BoardRepository(IGraphClient client)
            : base(client)
        {
            _generator = new Generator();
        }

        /// <summary>
        /// Return all the root boards of a user.
        /// <param name="username">User to get the boards from.</param>
        /// </summary>
        public async Task<IEnumerable<Board>> GetAllAsync(string username)
        {
            var result = await Client.Cypher
                .Match("(user:User)-[:ChildGroup]->(:Group)-[:Next*]->(boardOrLink)-[:Next*]->(:End)")
                .Where((User user) => user.Username == username)
                .OptionalMatch("(boardOrLink)-[:SharedBoard]->(shared:Board)")
                .Return((boardOrLink, shared) => Return.As<IEnumerable<Board>>(
                            "collect(boardOrLink) + collect(shared{.*, shared:true})"))
                .ResultsAsync;

            return result.Single();
        }

        /// <summary>
        /// Return the groups and boards from a parent board.
        /// </summary>
        /// <param name="id">Board id</param>
        public async Task<dynamic> GetAsync(string id, string username)
        {
            var result = await Client.Cypher
                .Match("(board:Board)")
                .Where((Board board) => board.Id == id)
                .OptionalMatch("(board)-[groupRel:ChildGroup]->(group:Group)")
                .OptionalMatch("(group)-[:Next*]->(childBoard:Board)-[:Next*]->(:End)")
                .With("board, group, groupRel, {group: group, boards: collect(childBoard)} AS groups")
                .OrderBy("groupRel.order")
                .OptionalMatch("path=(board)<-[:ChildBoard*0..]-()<-[:ChildBoard|SharedBoard]-(user:User)")
                .Where((User user) => user.Username == username)
                .With("board, group, groups, path")
                .Return((board, group, groups, path) => new
                {
                    Board = board.As<Board>(),
                    Groups = Return.As<IEnumerable<Groups>>("CASE WHEN group IS NULL THEN NULL ELSE collect(groups) END"),
                    Ancestors = Return.As<IEnumerable<Board>>("tail([b in nodes(path) WHERE (b:Board) | b])"),
                    UserAccess = Return.As<string>("CASE WHEN path IS NULL THEN 'false' ELSE 'true' END")
                })
                .ResultsAsync;

            return result.SingleOrDefault();
        }

        /// <summary>
        /// Checks if a user has access to a board.
        /// </summary>
        public async Task<bool> UserHasAccess(string boardId, string username)
        {
            var result = await Client.Cypher
                .Match("path=(board:Board)<-[:ChildBoard*0..]-()<-[:ChildBoard|SharedBoard]-(user:User)")
                .Where((User user) => user.Username == username)
                .AndWhere((Board board) => board.Id == boardId)
                .Return((path) => Return.As<string>("CASE WHEN path IS NULL THEN 'false' ELSE 'true' END"))
                .ResultsAsync;

            return result.SingleOrDefault() == "true";
        }

        /// <summary>
        /// Add a root board to a user.
        /// </summary>
        /// <remarks>
        /// Board gets added at the start.
        /// </remarks>
        /// <param name="entity">Board object</param>
        /// <param name="username">User to add it to.</param>
        public async Task<string> AddAsync(Board entity, string username)
        {
            string id = _generator.NewId(username);
            entity.Id = id;

            await Client.Cypher
                .Match("(user:User)")
                .Where((User user) => user.Username == username)
                .Call("apoc.lock.nodes([user])")
                .Match("(user)-[:ChildGroup]->(previous)-[oldRel:Next]->(next)")
                .Create("(previous)-[:Next]->(board:Board {newBoard})-[:Next]->(next)")
                .WithParam("newBoard", entity)
                .Create("(user)-[:ChildBoard]->(board)")
                .Delete("oldRel")
                .ExecuteWithoutResultsAsync();

            return id;
        }

        /// <summary>
        /// Add a board to a parent board.
        /// </summary>
        /// <remarks>
        /// Board gets added at the end.
        /// </remarks>
        /// <param name="entity">Board object</param>
        /// <param name="groupId">Id of group to add it to</param>
        /// <param name="username">Username of the owner</param>
        public async Task<string> AddAsync(Board entity, string groupId, string username)
        {
            string id = _generator.NewId(username);
            entity.Id = id;

            await Client.Cypher
                .Match("(parent:Board)-[:ChildGroup]->(group:Group)")
                .Where((Group group) => group.Id == groupId)
                .Call("apoc.lock.nodes([group])")
                .Match("(group)-[:Next*]->(next:End)")
                .Match("(previous)-[oldRel:Next]->(next)")
                .Create("(previous)-[:Next]->(board:Board {newBoard})-[:Next]->(next)")
                .WithParam("newBoard", entity)
                .Create("(parent)-[:ChildBoard]->(board)")
                .Delete("oldRel")
                .ExecuteWithoutResultsAsync();

            return id;
        }

        public async Task EditAsync(Board newBoardContents)
        {
            await Client.Cypher
                .Match("(board:Board)")
                .Where("board.id = {id}")
                .WithParam("id", newBoardContents.Id)
                .Set("board = {newBoardContents}")
                .WithParam("newBoardContents", newBoardContents)
                .ExecuteWithoutResultsAsync();
        }

        public async Task<bool> Exists(string id)
        {
            var result = await Client.Cypher
                .Match("(board:Board)")
                .Where("board.id = {id}")
                .WithParam("id", id)
                .Return((board) => Return.As<int>("count(board)"))
                .ResultsAsync;

            return result.Single() > 0;
        }

        public async Task DeleteAsync(string id)
        {
            await Client.Cypher
                .Match("(prev)-[:Next]->(board:Board)")
                .Where("board.id = {id}")
                .Call("apoc.lock.nodes([prev])")
                .Match("(prev)-[prevRel:Next]->(board)-[nextRel:Next]->(next)")
                .WithParam("id", id)
                .Match("(:Board)-[childRel:ChildBoard]->(board)")
                .Create("(prev)-[:Next]->(next)")
                .Delete("prevRel, nextRel, board, childRel")
                .ExecuteWithoutResultsAsync();
        }

        /// <summary>
        /// Add board groups
        /// </summary>
        public async Task<object> SetupAsync(string id)
        {
            var result =
                await Client.Cypher
                    .Match("(board:Board)")
                    .Where((Board board) => board.Id == id)
                    .OptionalMatch("(board)-[:ChildGroup]->(group:Group)")
                    .Return<int>("count(group)")
                    .ResultsAsync;

            if (result.Single() == 0) return await AddDefaultGroups(id);
            else                      throw new InvalidOperationException();
        }

        /// <summary>
        /// Move a board to under another board (or group)
        /// <param name="hostId">Id of the parent board</param>
        /// <param name="boardId">Id of board to move</param>
        /// <param name="targetId">Id of board to put it under</param>
        /// <param name="isRoot">Whether or not the board is a root board</param>
        /// </summary>
        public async Task MoveAsync(string hostId, string boardId, string targetId, bool isRoot)
        {
            Console.WriteLine("Moved");
            string whereHostId = "host.id = {hostId}";
            if (isRoot) whereHostId = "host.username = {hostId}"; // Username

            await Client.Cypher
                .Match("(host)")
                .Where(whereHostId)
                .Call("apoc.lock.nodes([host])")
                .WithParam("hostId", hostId)
                .Match("(previous)-[previousRel:Next]->(board:Board)-[nextRel:Next]->(next)")
                .Where((Board board) => board.Id == boardId)
                .Match("(newPrevious)-[rel:Next]->(newNext)")
                .Where("newPrevious.id = {targetId}")
                .WithParam("targetId", targetId)
                .Delete("previousRel, nextRel, rel")
                .Create("(previous)-[:Next]->(next)")
                .Create("(newPrevious)-[:Next]->(board)-[:Next]->(newNext)")
                .ExecuteWithoutResultsAsync();
        }

        /// <summary>
        /// Add a user to board for collaboration
        /// </summary>
        /// <param name="boardId">Id of the relevant board</param>
        /// <param name="username">Username of user to add</param>
        public async Task<bool> AddUserAsync(string boardId, string username)
        {
            var result = await Client.Cypher
                .Match("(user:User)")
                .Where((User user) => user.Username == username)
                .Call("apoc.lock.nodes([user])")
                .Match("(sharedBoard:Board)", "(user)-[:ChildGroup]->(previous)-[oldRel:Next]->(next)")
                .Where((Board sharedBoard) => sharedBoard.Id == boardId)
                .Create("(previous)-[:Next]->(link:Link)-[:Next]->(next)")
                .Delete("oldRel")
                .Create("(link)-[:SharedBoard]->(sharedBoard)")
                .Return((user) => user.As<User>().Username)
                .ResultsAsync;

            return result.Count() == 1; // If no users were found, return false. // TODO: Exception?
        }

        /// <summary>
        /// Remove a user from being able to edit the board
        /// </summary>
        /// <param name="boardId">Id of the relevant board</param>
        /// <param name="username">Username of user to remove</param>
        public async Task RemoveUserAsync(string boardId, string username)
        {
            await Client.Cypher
                .Match("(user:User)")
                .Where((User user) => user.Username == username)
                .Call("apoc.lock.nodes([user])")
                .Match("(user)-[:ChildGroup]->()-[:Next*]->(link:Link)-[sharedRel:SharedBoard]->(board:Board)")
                .Where((Board board) => board.Id == boardId)
                .Match("(previous)-[previousRel:Next]->(link)-[nextRel:Next]->(next)")
                .Delete("previousRel, nextRel, sharedRel, link")
                .Create("(previous)-[:Next]->(next)")
                .ExecuteWithoutResultsAsync();
        }

        /// <summary>
        /// Return the users the board is shared to (excluding the owner).
        /// </summary>
        /// <param name="boardId">Id of board that is being shared</param>
        public async Task<object> GetUsersAsync(string boardId)
        {
            return await Client.Cypher
                .Match("(board:Board)")
                .Where((Board board) => board.Id == boardId)
                .Match("(user:User)-[:ChildGroup]->(:Group)-[:Next]->(:Link)-[:SharedBoard]->(board)")
                .Return<string>("user.username")
                .ResultsAsync;
        }

        // This is all temporary ok
        private async Task<object> AddDefaultGroups(string id)
        {
            await Client.Cypher
                .Match("(board:Board)")
                .Where((Board board) => board.Id == id)
                .Create("(board)-[:ChildGroup { order: 0 }]->(g1:Group { name: 'Backlog' })-[:Next]->(:End)")
                .Set("g1.id = '" + _generator.NewId(id + "1") + "'")

                .Create("(board)-[:ChildGroup { order: 1 }]->(g2:Group { name: 'Ready' })-[:Next]->(:End)")
                .Set("g2.id = '" + _generator.NewId(id + "2") + "'")

                .Create("(board)-[:ChildGroup { order: 2 }]->(g3:Group { name: 'In Progress' })-[:Next]->(:End)")
                .Set("g3.id = '" + _generator.NewId(id + "3") + "'")

                .Create("(board)-[:ChildGroup { order: 3 }]->(g4:Group { name: 'Done' })-[:Next]->(:End)")
                .Set("g4.id = '" + _generator.NewId(id + "4") + "'")
                .ExecuteWithoutResultsAsync();

            return await Client.Cypher
                .Match("(board:Board)-[groupRel:ChildGroup]->(group:Group)")
                .Where((Board board) => board.Id == id)
                .With("group, groupRel")
                .OrderBy("groupRel.order")
                .Return((group) => group.As<Group>())
                .ResultsAsync;
        }
    }
}
